using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ataman.Core.Model;
using Ataman.Core.Pipeline;
using Ataman.Core.Settings;
using Ataman.Core.Voice;

namespace AtamanAssistant.ViewModels;

/// <summary>
/// Shell view-model backing the main window. Hosts the conversation surface,
/// binds to <see cref="IAssistantEngine"/> for responsive orchestration,
/// and provides smooth token streaming.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly ISettingsStore _settingsStore;
    private readonly IAssistantEngine? _engine;
    private TranscriptItem? _currentStreamingItem;
    private bool _busy;

    public MainViewModel(
        ISettingsStore settingsStore,
        IAssistantEngine? engine = null)
    {
        _settingsStore = settingsStore;
        _engine = engine ?? AppServices.CreateDefaultEngine(settingsStore);

        Settings = new SettingsViewModel(settingsStore);
        StatusText = "Hazır — Ayarlar sekmesinden dili ve modeli seçin.";

        WireEngine();
    }

    public MainViewModel(
        ISettingsStore settingsStore,
        ILanguageModel? languageModel,
        IAudioCapture? audioCapture,
        ISpeechToText? speechToText,
        IKeywordSpotter? keywordSpotter,
        ITextToSpeech? textToSpeech)
        : this(settingsStore, CreateEngineFromLegacyParams(settingsStore, languageModel, audioCapture, speechToText, keywordSpotter, textToSpeech))
    {
    }

    public ObservableCollection<TranscriptItem> Transcript { get; } = new();

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

    [ObservableProperty]
    private string inputText = string.Empty;

    private static IAssistantEngine? CreateEngineFromLegacyParams(
        ISettingsStore settingsStore,
        ILanguageModel? languageModel,
        IAudioCapture? audioCapture,
        ISpeechToText? speechToText,
        IKeywordSpotter? keywordSpotter,
        ITextToSpeech? textToSpeech)
    {
        if (languageModel is null)
        {
            return null;
        }

        return new AssistantEngine(
            settingsStore,
            languageModel,
            audioCapture,
            speechToText,
            keywordSpotter,
            textToSpeech,
            AppServices.SoundPlayerFactory?.Invoke());
    }

    private void WireEngine()
    {
        if (_engine is null)
        {
            return;
        }

        _engine.StatusChanged += (_, status) =>
        {
            Dispatcher.UIThread.Post(() => StatusText = status);
        };

        _engine.StateChanged += (_, state) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsThinking = state == AssistantState.Thinking;
                IsMicTesting = state is AssistantState.WaitingForWakeWord
                                      or AssistantState.WakingUp
                                      or AssistantState.ListeningForCommand;
                MicTestButtonText = IsMicTesting ? "Dinlemeyi durdur" : "Mikrofonu dinle";
            });
        };

        _engine.WakeWordDetected += (_, word) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText = $"Uyandırıldı (\"{word}\") — dinleniyor…";
            });
        };

        _engine.PartialSpeechRecognized += (_, partial) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText = $"… {partial}";
            });
        };

        _engine.TranscriptAdded += (_, entry) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (entry.Role == ChatRole.User)
                {
                    Transcript.Add(new TranscriptItem(ChatRole.User, entry.Text, entry.TranslatedText, timestamp: entry.Timestamp));
                    // Prepare streaming response placeholder for assistant
                    _currentStreamingItem = new TranscriptItem(ChatRole.Assistant, string.Empty, isStreaming: true);
                    Transcript.Add(_currentStreamingItem);
                }
                else if (entry.Role == ChatRole.Assistant)
                {
                    if (_currentStreamingItem is not null)
                    {
                        _currentStreamingItem.Text = entry.Text;
                        _currentStreamingItem.TranslatedText = entry.TranslatedText;
                        _currentStreamingItem.IsStreaming = false;
                        _currentStreamingItem = null;
                    }
                    else
                    {
                        Transcript.Add(new TranscriptItem(ChatRole.Assistant, entry.Text, entry.TranslatedText, timestamp: entry.Timestamp));
                    }
                }
            });
        };

        _engine.TokenProduced += (_, token) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_currentStreamingItem is not null)
                {
                    _currentStreamingItem.AppendToken(token);
                }
            });
        };
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var input = InputText.Trim();
        if (input.Length == 0)
        {
            return;
        }

        InputText = string.Empty;

        if (_engine is null)
        {
            Transcript.Add(new TranscriptItem(ChatRole.User, input));
            Transcript.Add(new TranscriptItem(ChatRole.Assistant, "LLM motoru bu platforma bağlanmadı."));
            return;
        }

        if (_busy)
        {
            StatusText = "Asistan meşgul, lütfen bekleyin.";
            return;
        }

        _busy = true;
        try
        {
            await _engine.ProcessCommandAsync(input);
        }
        finally
        {
            _busy = false;
        }
    }

    [RelayCommand]
    private void ClearChat()
    {
        Transcript.Clear();
        _currentStreamingItem = null;
        StatusText = "Yeni sohbet başlatıldı.";
    }

    [RelayCommand]
    private async Task ToggleMicTestAsync()
    {
        if (_engine is null)
        {
            StatusText = "Ses girişi bu platformda henüz yok.";
            return;
        }

        if (IsMicTesting)
        {
            await _engine.StopListeningAsync();
        }
        else
        {
            await _engine.StartListeningAsync();
        }
    }
}