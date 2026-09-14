using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ataman.Core;
using Ataman.Core.Model;
using Ataman.Core.Settings;
using Ataman.Core.Voice;

namespace AtamanAssistant.ViewModels;

/// <summary>
/// Shell view-model backing the main window. Hosts the conversation surface
/// and the settings page. Phase 2 wires text + voice input through the on-device
/// LLM (llama.cpp via LLamaSharp).
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly ISettingsStore _settingsStore;
    private readonly ILanguageModel? _languageModel;
    private readonly IAudioCapture? _audioCapture;
    private readonly ISpeechToText? _speechToText;
    private readonly IKeywordSpotter? _keywordSpotter;
    private readonly ITextToSpeech? _textToSpeech;

    private readonly List<ChatMessage> _chat = new();
    private string? _loadedModelId;
    private bool _busy;

    public MainViewModel(
        ISettingsStore settingsStore,
        ILanguageModel? languageModel = null,
        IAudioCapture? audioCapture = null,
        ISpeechToText? speechToText = null,
        IKeywordSpotter? keywordSpotter = null,
        ITextToSpeech? textToSpeech = null)
    {
        _settingsStore = settingsStore;
        _languageModel = languageModel;
        _audioCapture = audioCapture;
        _speechToText = speechToText;
        _keywordSpotter = keywordSpotter;
        _textToSpeech = textToSpeech;

        if (_languageModel is not null)
        {
            _languageModel.TokenProduced += OnTokenProduced;
        }

        Settings = new SettingsViewModel(settingsStore);
        StatusText = "Hazır — Ayarlar sekmesinden dili ve modeli seçin.";
    }

    public ObservableCollection<string> Transcript { get; } = new();

    [ObservableProperty]
    private SettingsViewModel settings;

    [ObservableProperty]
    private string statusText;

    [ObservableProperty]
    private bool isMicTesting;

    [ObservableProperty]
    private string micTestButtonText = "Mikrofonu dinle";

    [ObservableProperty]
    private bool isThinking;

    /// <summary>Status/caption mirror for <see cref="IsMicTesting"/>.</summary>
    partial void OnIsMicTestingChanged(bool value) => MicTestButtonText = value ? "Dinlemeyi durdur" : "Mikrofonu dinle";

    [ObservableProperty]
    private string inputText = string.Empty;

    [RelayCommand]
    private async Task SendAsync()
    {
        var input = InputText.Trim();
        if (input.Length == 0)
        {
            return;
        }

        InputText = string.Empty;
        await RunAssistantTurnAsync(input);
    }

    [RelayCommand]
    private void ClearChat()
    {
        _chat.Clear();
        Transcript.Clear();
        if (_textToSpeech is not null)
        {
            _ = _textToSpeech.StopSpeakingAsync();
        }

        StatusText = "Yeni sohbet başlatıldı.";
    }

    /// <summary>
    /// Phase 2 core: streams the microphone through Vosk; every finalized
    /// utterance is treated as a user turn for the LLM.
    /// </summary>
    [RelayCommand]
    private async Task ToggleMicTestAsync()
    {
        if (IsMicTesting)
        {
            TeardownMicTest();
            return;
        }

        if (_audioCapture is null || _speechToText is null)
        {
            StatusText = "Ses girişi bu platformda henüz yok.";
            return;
        }

        try
        {
            var settings = await _settingsStore.LoadAsync().ConfigureAwait(true);
            var language = LanguageCatalog.ForCode(settings.ConversationLanguage);

            StatusText = "Vosk modeli kontrol ediliyor…";
            var modelDir = await EnsureVoskModelAsync(language).ConfigureAwait(true);

            StatusText = "Model yükleniyor…";
            await _speechToText.LoadModelAsync(modelDir).ConfigureAwait(true);

            if (_keywordSpotter is not null)
            {
                await _keywordSpotter.LoadAsync(modelDir).ConfigureAwait(true);
                _keywordSpotter.SetKeywords(new[] { settings.WakeWord });
                _keywordSpotter.WakeWordDetected += OnWakeWordDetected;
            }

            _speechToText.Recognized += OnRecognized;
            _speechToText.PartialResult += OnPartialResult;
            _audioCapture.AudioFrameReceived += OnAudioFrame;

            await _audioCapture.StartAsync().ConfigureAwait(true);
            IsMicTesting = true;
            StatusText = $"Dinleniyor… ({language.DisplayName})";
        }
        catch (Exception ex)
        {
            TeardownMicTest();
            StatusText = $"Hata: {ex.Message}";
        }
    }

    private void TeardownMicTest()
    {
        if (_audioCapture is not null)
        {
            _audioCapture.AudioFrameReceived -= OnAudioFrame;
            _ = _audioCapture.StopAsync();
        }

        if (_speechToText is not null)
        {
            _speechToText.Recognized -= OnRecognized;
            _speechToText.PartialResult -= OnPartialResult;
        }

        if (_keywordSpotter is not null)
        {
            _keywordSpotter.WakeWordDetected -= OnWakeWordDetected;
        }

        IsMicTesting = false;
        StatusText = "Hazır";
    }

    private void OnAudioFrame(object? sender, AudioFrameEventArgs e)
    {
        _speechToText?.Feed(e.Pcm);
        _keywordSpotter?.Feed(e.Pcm);
    }

    private void OnPartialResult(object? sender, string partial)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsMicTesting)
            {
                StatusText = $"… {partial}";
            }
        });
    }

    private void OnRecognized(object? sender, FinalRecognition result)
    {
        var text = result.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (IsMicTesting)
            {
                StatusText = "Dinliyorum…";
            }

            _ = RunAssistantTurnAsync(text, isVoice: true);
        });
    }

    private void OnWakeWordDetected(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Transcript.Add("[Wake] Uyandırma kelimesi algılandı.");
            StatusText = "Uyandırıldı — komut dinleniyor…";
        });
    }

    private void OnTokenProduced(object? sender, string token)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Transcript.Count > 0)
            {
                Transcript[^1] += token;
            }
        });
    }

    private async Task RunAssistantTurnAsync(string userText, bool isVoice = false)
    {
        if (_busy)
        {
            StatusText = "Yanıt bekleniyor, sonra tekrar deneyin.";
            return;
        }

        if (_languageModel is null)
        {
            Transcript.Add($"Sen: {userText}");
            Transcript.Add("Ataman: LLM motoru bu platforma bağlanmadı (Faz 2 'devam' bekliyor).");
            return;
        }

        _busy = true;
        try
        {
            var settings = await _settingsStore.LoadAsync().ConfigureAwait(true);
            var language = LanguageCatalog.ForCode(settings.ConversationLanguage);

            Transcript.Add($"Sen: {userText}");
            _chat.Add(new ChatMessage(ChatRole.User, userText));

            if (!await EnsureModelLoadedAsync(settings, language).ConfigureAwait(true))
            {
                return;
            }

            StatusText = "Düşünüyor…";
            IsThinking = true;
            Transcript.Add("Ataman: ");

            var messages = BuildMessages(settings, language);
            var response = await Task.Run(async () => await _languageModel.CompleteAsync(
                messages,
                new GenerationOptions(
                    Temperature: settings.Temperature,
                    MaxTokens: settings.MaxResponseTokens),
                CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(true);

            if (Transcript.Count > 0 && Transcript[^1].StartsWith("Ataman: "))
            {
                Transcript[^1] = "Ataman: " + response;
            }

            _chat.Add(new ChatMessage(ChatRole.Assistant, response));
            StatusText = isVoice ? "Dinliyorum…" : "Hazır";

            if (_textToSpeech is not null && settings.SpeakResponses)
            {
                await SpeakResponseAsync(response).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "İptal edildi.";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ataman] LLM error: {ex}");
            StatusText = $"Hata: {ex.Message}";
            Transcript.Add($"Ataman: [hata] {ex.Message}");
        }
        finally
        {
            IsThinking = false;
            _busy = false;
        }
    }

    private async Task SpeakResponseAsync(string response)
    {
        try
        {
            var settings = await _settingsStore.LoadAsync().ConfigureAwait(true);
            await _textToSpeech!.SetVoiceAsync(VoiceCatalog.ForId(settings.TtsVoiceId)).ConfigureAwait(true);
            await _textToSpeech.SpeakAsync(response, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = $"Seslendirme hatası: {ex.Message}";
        }
    }

    private IReadOnlyList<ChatMessage> BuildMessages(AssistantSettings settings, LanguageInfo language)
    {
        var system = settings.ConversationLanguage.Equals("tr", StringComparison.OrdinalIgnoreCase)
            ? "Sen 'Ataman Assistant' adlı, tamamen cihaz içinde çalışan, çevrimdışı bir sesli asistan. "
              + "Kısa, net ve doğal Türkçe yanıtlar ver. Konuşmaya uygun, önemsiz detayları atla. "
              + "Yalnızca gerçekten emin olduğun konularda ayrıntı ver."
            : "You are 'Ataman Assistant', an on-device, fully offline voice assistant. "
              + "Answer concisely, naturally and helpfully, in a style suited to speech. Avoid filler.";

        return new List<ChatMessage> { new(ChatRole.System, system) }
            .Concat(_chat)
            .ToList();
    }

    /// <summary>Ensures the selected GGUF is downloaded and loaded into the LLM.</summary>
    private async Task<bool> EnsureModelLoadedAsync(AssistantSettings settings, LanguageInfo language)
    {
        var info = ModelCatalog.ForId(settings.ModelId);
        var path = settings.ModelFilePath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusText = "Model indirilmedi — Ayarlar sekmesinde önce indirin.";
            _chat.RemoveAt(_chat.Count - 1);
            Transcript.RemoveAt(Transcript.Count - 1);
            return false;
        }

        if (_loadedModelId == info.Id && _languageModel!.IsLoaded)
        {
            return true;
        }

        StatusText = "LLM modeli yükleniyor (ilk seferde uzun sürebilir)…";
        IsThinking = true;
        var spec = new ModelSpec(
            info.Id,
            path,
            ContextTokens: info.RecommendedContextTokens,
            GpuLayers: 0,
            Threads: Environment.ProcessorCount);

        try
        {
            await Task.Run(async () => await _languageModel!.LoadAsync(spec).ConfigureAwait(false)).ConfigureAwait(true);
            _loadedModelId = info.Id;
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ataman] model failed to load: {ex}");
            StatusText = $"Model yüklenemedi: {ex.Message}";
            return false;
        }
        finally
        {
            IsThinking = false;
        }
    }

    private async Task<string> EnsureVoskModelAsync(LanguageInfo language)
    {
        var manager = new ModelManager(AppPaths.VoskModels);
        var extractRoot = AppPaths.VoskModels;
        var modelDir = Path.Combine(extractRoot, language.VoskSmallModelId);
        if (Directory.Exists(modelDir))
        {
            return modelDir;
        }

        StatusText = $"Vosk modeli indiriliyor ({language.VoskSmallModelId})…";
        var url = $"https://alphacephei.com/vosk/models/{language.VoskSmallModelId}.zip";
        var progress = new Progress<double>(p => StatusText = $"Vosk modeli indiriliyor… %{p * 100:0}");
        return await manager.DownloadAndExtractZipAsync(url, extractRoot, progress).ConfigureAwait(true);
    }
}