using System.Text;
using Ataman.Core.Model;
using Ataman.Core.Settings;
using Ataman.Core.Translate;
using Ataman.Core.Voice;

namespace Ataman.Core.Pipeline;

/// <summary>
/// Core orchestrator for Ataman Assistant.
/// Implements a deterministic voice state machine:
/// Idle -> WaitingForWakeWord -> WakingUp (chime) -> ListeningForCommand -> Thinking -> Speaking -> WaitingForWakeWord.
/// Microphone is muted during Thinking and Speaking to completely prevent acoustic feedback.
/// Input prompts in any language are translated locally to English for the LLM,
/// and LLM outputs are translated into the configured conversation language.
/// </summary>
public sealed class AssistantEngine : IAssistantEngine
{
    private readonly ISettingsStore _settingsStore;
    private readonly ILanguageModel _languageModel;
    private readonly IAudioCapture? _audioCapture;
    private readonly ISpeechToText? _speechToText;
    private readonly IKeywordSpotter? _keywordSpotter;
    private readonly ITextToSpeech? _textToSpeech;
    private readonly ISoundPlayer? _soundPlayer;
    private readonly ITranslator _translator;

    private readonly object _stateLock = new();
    private readonly List<TranscriptEntry> _transcript = new();
    private readonly List<ChatMessage> _conversation = new();

    private AssistantState _state = AssistantState.Idle;
    private string? _loadedModelId;
    private bool _isListeningActive;
    private volatile bool _isStreamingToTranscript;
    private CancellationTokenSource? _listeningCts;

    public AssistantEngine(
        ISettingsStore settingsStore,
        ILanguageModel languageModel,
        IAudioCapture? audioCapture = null,
        ISpeechToText? speechToText = null,
        IKeywordSpotter? keywordSpotter = null,
        ITextToSpeech? textToSpeech = null,
        ISoundPlayer? soundPlayer = null,
        ITranslator? translator = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _languageModel = languageModel ?? throw new ArgumentNullException(nameof(languageModel));
        _audioCapture = audioCapture;
        _speechToText = speechToText;
        _keywordSpotter = keywordSpotter;
        _textToSpeech = textToSpeech;
        _soundPlayer = soundPlayer;
        _translator = translator ?? new LlmTranslator(_languageModel);

        _languageModel.TokenProduced += OnTokenProduced;
    }

    public event EventHandler<string>? WakeWordDetected;
    public event EventHandler<TranscriptEntry>? TranscriptAdded;
    public event EventHandler<string>? TokenProduced;
    public event EventHandler<string>? PartialSpeechRecognized;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<AssistantState>? StateChanged;

    public AssistantState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
        private set
        {
            lock (_stateLock)
            {
                if (_state == value)
                {
                    return;
                }

                _state = value;
            }

            StateChanged?.Invoke(this, value);
        }
    }

    public IReadOnlyList<TranscriptEntry> Transcript
    {
        get
        {
            lock (_stateLock)
            {
                return _transcript.ToList();
            }
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        await EnsureModelLoadedAsync(settings, ct).ConfigureAwait(false);
        SetStatus("Hazır");
    }

    public async Task StartListeningAsync(CancellationToken ct = default)
    {
        if (_audioCapture is null || _speechToText is null)
        {
            SetStatus("Ses girişi bu platformda kullanılamıyor.");
            return;
        }

        var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        var language = LanguageCatalog.ForCode(settings.ConversationLanguage);

        SetStatus($"Vosk modeli hazırlanıyor ({language.DisplayName})…");
        var voskModelDir = await EnsureVoskModelAsync(language, ct).ConfigureAwait(false);

        SetStatus("Konuşma tanıyıcı yükleniyor…");
        await _speechToText.LoadModelAsync(voskModelDir, ct).ConfigureAwait(false);
        _speechToText.Reset();

        if (_keywordSpotter is not null)
        {
            await _keywordSpotter.LoadAsync(voskModelDir, ct).ConfigureAwait(false);
            var wakeWord = string.IsNullOrWhiteSpace(settings.WakeWord) ? language.DefaultWakeWord : settings.WakeWord.Trim();
            var keywords = wakeWord.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _keywordSpotter.SetKeywords(keywords);
            _keywordSpotter.Reset();
            _keywordSpotter.WakeWordDetected -= OnWakeWordDetected;
            _keywordSpotter.WakeWordDetected += OnWakeWordDetected;
        }

        _speechToText.Recognized -= OnSpeechRecognized;
        _speechToText.Recognized += OnSpeechRecognized;
        _speechToText.PartialResult -= OnSpeechPartialResult;
        _speechToText.PartialResult += OnSpeechPartialResult;

        _audioCapture.AudioFrameReceived -= OnAudioFrameReceived;
        _audioCapture.AudioFrameReceived += OnAudioFrameReceived;

        _listeningCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await _audioCapture.StartAsync(_listeningCts.Token).ConfigureAwait(false);
        _isListeningActive = true;

        if (_keywordSpotter is not null && settings.AlwaysListening)
        {
            State = AssistantState.WaitingForWakeWord;
            SetStatus($"Uyku modunda — uyandırma sözcüğü bekleniyor: \"{settings.WakeWord}\"");
        }
        else
        {
            State = AssistantState.ListeningForCommand;
            SetStatus($"Dinleniyor… ({language.DisplayName})");
        }
    }

    public async Task StopListeningAsync()
    {
        _isListeningActive = false;
        _listeningCts?.Cancel();

        if (_audioCapture is not null)
        {
            _audioCapture.AudioFrameReceived -= OnAudioFrameReceived;
            await _audioCapture.StopAsync().ConfigureAwait(false);
        }

        if (_speechToText is not null)
        {
            _speechToText.Recognized -= OnSpeechRecognized;
            _speechToText.PartialResult -= OnSpeechPartialResult;
        }

        if (_keywordSpotter is not null)
        {
            _keywordSpotter.WakeWordDetected -= OnWakeWordDetected;
        }

        State = AssistantState.Idle;
        SetStatus("Hazır");
    }

    public async Task<string> ProcessCommandAsync(string text, CancellationToken ct = default)
    {
        var input = text.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        State = AssistantState.Thinking;
        var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        var convLang = LanguageCatalog.ForCode(settings.ConversationLanguage);
        var isEnglishConv = convLang.Code.Equals("en", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (!await EnsureModelLoadedAsync(settings, ct).ConfigureAwait(false))
            {
                var errorEntry = new TranscriptEntry(DateTimeOffset.Now, ChatRole.Assistant, "Dil modeli yüklenemedi. Lütfen ayarları kontrol edin.");
                lock (_stateLock)
                {
                    _transcript.Add(errorEntry);
                }
                TranscriptAdded?.Invoke(this, errorEntry);
                return string.Empty;
            }

            // Step 1: Translate prompt to English if entered in any other language
            SetStatus("İstem İngilizceye çevriliyor…");
            _isStreamingToTranscript = false;
            string promptInEnglish = input;
            try
            {
                promptInEnglish = await _translator.TranslateAsync(input, convLang.DisplayName, "English", ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(promptInEnglish))
                {
                    promptInEnglish = input;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AssistantEngine] Prompt translation failed: {ex.Message}");
                promptInEnglish = input;
            }

            // Record user turn in UI transcript
            var userEntry = new TranscriptEntry(
                DateTimeOffset.Now,
                ChatRole.User,
                input,
                string.Equals(promptInEnglish, input, StringComparison.OrdinalIgnoreCase) ? null : promptInEnglish);
            lock (_stateLock)
            {
                _transcript.Add(userEntry);
            }
            TranscriptAdded?.Invoke(this, userEntry);

            // Step 2: Query LLM in English
            SetStatus("Düşünüyor…");
            var systemPrompt =
                "You are Ataman Assistant, a helpful and accurate on-device voice assistant. " +
                "Answer concisely, helpfully and naturally in spoken English style. Avoid bullet points, preamble, or conversational filler.";

            var messages = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };
            messages.AddRange(_conversation);
            messages.Add(new ChatMessage(ChatRole.User, promptInEnglish));

            var options = new GenerationOptions(
                Temperature: settings.Temperature,
                MaxTokens: settings.MaxResponseTokens);

            // If conversation language is English, stream tokens live to transcript
            _isStreamingToTranscript = isEnglishConv;
            var llmResponseEnglish = await _languageModel.CompleteAsync(messages, options, ct).ConfigureAwait(false);
            _isStreamingToTranscript = false;

            // Keep pure English history in the conversation context for LLM multi-turn reasoning
            _conversation.Add(new ChatMessage(ChatRole.User, promptInEnglish));
            _conversation.Add(new ChatMessage(ChatRole.Assistant, llmResponseEnglish));

            // Step 3: Translate response into the configured conversation language if not English
            string finalResponse = llmResponseEnglish;
            if (!isEnglishConv)
            {
                SetStatus($"Yanıt {convLang.DisplayName} diline çevriliyor…");
                _isStreamingToTranscript = true; // Stream the translated tokens live!
                try
                {
                    finalResponse = await _translator.TranslateAsync(llmResponseEnglish, "English", convLang.DisplayName, ct).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(finalResponse))
                    {
                        finalResponse = llmResponseEnglish;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[AssistantEngine] Response translation failed: {ex.Message}");
                    finalResponse = llmResponseEnglish;
                }
                finally
                {
                    _isStreamingToTranscript = false;
                }
            }

            var assistantEntry = new TranscriptEntry(
                DateTimeOffset.Now,
                ChatRole.Assistant,
                finalResponse,
                isEnglishConv ? null : llmResponseEnglish);

            lock (_stateLock)
            {
                _transcript.Add(assistantEntry);
            }
            TranscriptAdded?.Invoke(this, assistantEntry);

            // Step 4: Text-to-speech
            if (settings.SpeakResponses && _textToSpeech is not null)
            {
                State = AssistantState.Speaking;
                SetStatus("Yanıt seslendiriliyor…");
                try
                {
                    await _textToSpeech.SetVoiceAsync(VoiceCatalog.ForId(settings.TtsVoiceId), ct).ConfigureAwait(false);
                    await _textToSpeech.SpeakAsync(finalResponse, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[AssistantEngine] TTS error: {ex.Message}");
                }
            }

            return finalResponse;
        }
        catch (OperationCanceledException)
        {
            SetStatus("İptal edildi.");
            return string.Empty;
        }
        catch (Exception ex)
        {
            SetStatus($"Hata: {ex.Message}");
            State = AssistantState.Error;
            return string.Empty;
        }
        finally
        {
            _isStreamingToTranscript = false;

            // Transition back to listening if audio capture was running
            if (_isListeningActive)
            {
                _speechToText?.Reset();
                _keywordSpotter?.Reset();

                if (settings.AlwaysListening && _keywordSpotter is not null)
                {
                    State = AssistantState.WaitingForWakeWord;
                    SetStatus($"Uyku modunda — uyandırma sözcüğü bekleniyor: \"{settings.WakeWord}\"");
                }
                else
                {
                    State = AssistantState.ListeningForCommand;
                    SetStatus("Dinleniyor…");
                }
            }
            else
            {
                State = AssistantState.Idle;
                SetStatus("Hazır");
            }
        }
    }

    private void OnAudioFrameReceived(object? sender, AudioFrameEventArgs e)
    {
        var currentState = State;

        // Strict state gating: microphone audio only routes according to current state.
        // During Thinking and Speaking, audio is completely dropped to eliminate acoustic feedback!
        switch (currentState)
        {
            case AssistantState.WaitingForWakeWord:
                _keywordSpotter?.Feed(e.Pcm);
                break;

            case AssistantState.ListeningForCommand:
                _speechToText?.Feed(e.Pcm);
                break;
        }
    }

    private async void OnWakeWordDetected(object? sender, EventArgs e)
    {
        var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        WakeWordDetected?.Invoke(this, settings.WakeWord);

        State = AssistantState.WakingUp;
        SetStatus("Uyandırıldı!");

        if (settings.NotifyOnWake && _soundPlayer is not null)
        {
            var chimePcm = ToneGenerator.CreateWakeChime(AudioFormat.SampleRate);
            try
            {
                await _soundPlayer.PlayAsync(chimePcm, AudioFormat.SampleRate, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AssistantEngine] Wake chime error: {ex.Message}");
            }
        }

        _speechToText?.Reset();
        State = AssistantState.ListeningForCommand;
        SetStatus("Sizi dinliyorum…");
    }

    private void OnSpeechPartialResult(object? sender, string partial)
    {
        if (State == AssistantState.ListeningForCommand && !string.IsNullOrWhiteSpace(partial))
        {
            PartialSpeechRecognized?.Invoke(this, partial);
            SetStatus($"… {partial}");
        }
    }

    private void OnSpeechRecognized(object? sender, FinalRecognition result)
    {
        if (State != AssistantState.ListeningForCommand)
        {
            return;
        }

        var text = result.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessCommandAsync(text, _listeningCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AssistantEngine] Error processing voice turn: {ex}");
            }
        });
    }

    private void OnTokenProduced(object? sender, string token)
    {
        if (_isStreamingToTranscript)
        {
            TokenProduced?.Invoke(this, token);
        }
    }

    private async Task<bool> EnsureModelLoadedAsync(AssistantSettings settings, CancellationToken ct)
    {
        var info = ModelCatalog.ForId(settings.ModelId);
        var path = settings.ModelFilePath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            SetStatus("Model indirilmedi — Ayarlar sekmesinden modeli indirin.");
            return false;
        }

        if (_loadedModelId == info.Id && _languageModel.IsLoaded)
        {
            return true;
        }

        SetStatus("LLM modeli yükleniyor…");
        var spec = new ModelSpec(
            info.Id,
            path,
            ContextTokens: info.RecommendedContextTokens,
            GpuLayers: 0,
            Threads: Environment.ProcessorCount);

        try
        {
            await _languageModel.LoadAsync(spec, ct).ConfigureAwait(false);
            _loadedModelId = info.Id;
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AssistantEngine] LLM load failed: {ex}");
            SetStatus($"Model yüklenemedi: {ex.Message}");
            return false;
        }
    }

    private static async Task<string> EnsureVoskModelAsync(LanguageInfo language, CancellationToken ct)
    {
        var manager = new ModelManager(AppPaths.VoskModels);
        var modelDir = Path.Combine(AppPaths.VoskModels, language.VoskSmallModelId);
        if (Directory.Exists(modelDir) && ModelManager.IsModelDirectory(modelDir))
        {
            return modelDir;
        }

        var url = $"https://alphacephei.com/vosk/models/{language.VoskSmallModelId}.zip";
        return await manager.DownloadAndExtractZipAsync(url, AppPaths.VoskModels, ct: ct).ConfigureAwait(false);
    }

    private void SetStatus(string status)
    {
        StatusChanged?.Invoke(this, status);
    }

    public async ValueTask DisposeAsync()
    {
        await StopListeningAsync().ConfigureAwait(false);
        _languageModel.TokenProduced -= OnTokenProduced;
    }
}
