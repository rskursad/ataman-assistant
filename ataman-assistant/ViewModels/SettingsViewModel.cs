using System.Collections.ObjectModel;
using System.IO;
using Ataman.Core;
using Ataman.Core.Model;
using Ataman.Core.Settings;
using Ataman.Core.Voice;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AtamanAssistant.ViewModels;

/// <summary>Settings page view-model: language, wake word, TTS voice, model and fine-tune.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsStore _store;
    private readonly ModelManager _llmManager;

    private AssistantSettings? _original;

    public SettingsViewModel(ISettingsStore store, ModelManager? llmManager = null)
    {
        _store = store;
        _llmManager = llmManager ?? new ModelManager(AppPaths.LlmModels);

        Languages = new ObservableCollection<LanguageInfo>(LanguageCatalog.All);
        Models = new ObservableCollection<ModelInfo>(ModelCatalog.All);
        Voices = new ObservableCollection<TtsVoice>(VoiceCatalog.All);

        _ = InitializeAsync();
    }

    public ObservableCollection<LanguageInfo> Languages { get; }
    public ObservableCollection<ModelInfo> Models { get; }
    public ObservableCollection<TtsVoice> Voices { get; }

    [ObservableProperty]
    private LanguageInfo selectedLanguage = LanguageCatalog.English;

    [ObservableProperty]
    private ModelInfo selectedModel = ModelCatalog.Qwen25_1_5B;

    [ObservableProperty]
    private TtsVoice selectedVoice = VoiceCatalog.TurkishDfki;

    [ObservableProperty]
    private string wakeWord = "ataman";

    [ObservableProperty]
    private bool alwaysListening = true;

    [ObservableProperty]
    private bool translateToEnglish = true;

    [ObservableProperty]
    private bool speakResponses = true;

    [ObservableProperty]
    private int maxResponseTokens = 160;

    [ObservableProperty]
    private double temperature = 0.7;

    [ObservableProperty]
    private bool modelDownloaded;

    [ObservableProperty]
    private string modelStateText = "indirilmedi";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadModelCommand))]
    private bool isDownloading;

    [ObservableProperty]
    private double downloadProgress;

    [ObservableProperty]
    private string statusText = "Ayarlar yükleniyor…";

    [ObservableProperty]
    private bool isDirty;

    private async Task InitializeAsync()
    {
        try
        {
            _original = await _store.LoadAsync().ConfigureAwait(true);

            SelectedLanguage = LanguageCatalog.ForCode(_original.ConversationLanguage);
            SelectedModel = ModelCatalog.ForId(_original.ModelId);
            SelectedVoice = VoiceCatalog.ForId(_original.TtsVoiceId);
            WakeWord = string.IsNullOrWhiteSpace(_original.WakeWord) ? "ataman" : _original.WakeWord;
            AlwaysListening = _original.AlwaysListening;
            TranslateToEnglish = _original.TranslateToEnglish;
            SpeakResponses = _original.SpeakResponses;
            MaxResponseTokens = _original.MaxResponseTokens;
            Temperature = _original.Temperature;

            RefreshModelState();
            StatusText = "Ayarlar yüklendi.";
        }
        catch (Exception ex)
        {
            StatusText = "Ayarlar yüklenemedi: " + ex.Message;
        }
    }

    partial void OnTemperatureChanged(double value) => MarkDirty();
    partial void OnMaxResponseTokensChanged(int value) => MarkDirty();
    partial void OnWakeWordChanged(string value) => MarkDirty();
    partial void OnAlwaysListeningChanged(bool value) => MarkDirty();
    partial void OnTranslateToEnglishChanged(bool value) => MarkDirty();
    partial void OnSpeakResponsesChanged(bool value) => MarkDirty();
    partial void OnSelectedLanguageChanged(LanguageInfo value)
    {
        MarkDirty();
        if (value is not null)
        {
            WakeWord = value.DefaultWakeWord;
            SelectedVoice = VoiceCatalog.ForId(value.DefaultTtsVoiceId);
        }
    }
    partial void OnSelectedVoiceChanged(TtsVoice value) => MarkDirty();

    partial void OnSelectedModelChanged(ModelInfo value)
    {
        MarkDirty();
        RefreshModelState();
    }

    private void RefreshModelState()
    {
        var path = _llmManager.GetFilePath(SelectedModel.Id + ".gguf");
        var downloaded = _llmManager.IsDownloaded(path);
        ModelDownloaded = downloaded;
        ModelStateText = downloaded ? "indirildi" : "indirilmedi";
        if (downloaded && !IsDownloading)
        {
            StatusText = "Model dosyası zaten indirilmiş.";
        }
    }

    [RelayCommand]
    private void SetWakeWordPreset(string word)
    {
        if (!string.IsNullOrWhiteSpace(word))
        {
            WakeWord = word.Trim();
            MarkDirty();
        }
    }

    private void MarkDirty()
    {
        IsDirty = true;
        if (StatusText is "Ayarlar yüklendi." or "Ayarlar kaydedildi.")
        {
            StatusText = "Değişiklikler kaydedilmedi.";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var model = ModelCatalog.ForId(SelectedModel.Id);
            var language = LanguageCatalog.ForCode(SelectedLanguage.Code);

            _original ??= await _store.LoadAsync().ConfigureAwait(true);
            _original.ConversationLanguage = language.Code;
            _original.WakeWord = string.IsNullOrWhiteSpace(WakeWord) ? language.DefaultWakeWord : WakeWord.Trim();
            _original.AlwaysListening = AlwaysListening;
            _original.TranslateToEnglish = TranslateToEnglish;
            _original.SpeakResponses = SpeakResponses;
            _original.TtsVoiceId = SelectedVoice.Id;
            _original.MaxResponseTokens = MaxResponseTokens;
            _original.Temperature = (float)Temperature;

            if (_original.ModelId != model.Id)
            {
                _original.ModelId = model.Id;
                _original.ModelFilePath = _llmManager.GetFilePath(model.Id + ".gguf");
            }

            await _store.SaveAsync(_original).ConfigureAwait(true);

            IsDirty = false;
            StatusText = "Ayarlar kaydedildi.";
        }
        catch (Exception ex)
        {
            StatusText = "Kaydedilemedi: " + ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDownloadModel))]
    private async Task DownloadModelAsync()
    {
        try
        {
            IsDownloading = true;
            DownloadProgress = 0;
            StatusText = "Model indiriliyor… %0";

            var progress = new Progress<double>(
                p => { DownloadProgress = p; StatusText = $"Model indiriliyor… %{p * 100:0}"; });

            var file = await _llmManager.DownloadAsync(
                SelectedModel.DownloadUrl,
                _llmManager.GetFilePath(SelectedModel.Id + ".gguf"),
                progress);

            RefreshModelState();
            StatusText = $"Model hazır: {(file.Size / 1_000_000.0):0.0} MB";
        }
        catch (OperationCanceledException)
        {
            StatusText = "İndirme iptal edildi.";
        }
        catch (Exception ex)
        {
            StatusText = "İndirme başarısız: " + ex.Message;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private bool CanDownloadModel() => !IsDownloading;
}