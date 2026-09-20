using Ataman.Core.Model;
using Ataman.Core.Pipeline;
using Ataman.Core.Settings;
using Ataman.Core.Translate;
using Ataman.Core.Voice;

namespace AtamanAssistant;

/// <summary>
/// Platform-specific service factories. Each app head (Desktop, Android, iOS,
/// Browser) registers its native implementations at startup; the shared UI
/// project resolves them here so ViewModels never depend on platform types.
/// </summary>
public static class AppServices
{
    public static Func<ILanguageModel>? LanguageModelFactory { get; set; }
    public static Func<ISpeechToText>? SpeechToTextFactory { get; set; }
    public static Func<IKeywordSpotter>? KeywordSpotterFactory { get; set; }
    public static Func<IAudioCapture>? AudioCaptureFactory { get; set; }
    public static Func<ISoundPlayer>? SoundPlayerFactory { get; set; }
    public static Func<ITextToSpeech>? TextToSpeechFactory { get; set; }
    public static Func<ITranslator>? TranslatorFactory { get; set; }
    public static Func<ISettingsStore, IAssistantEngine>? AssistantEngineFactory { get; set; }

    public static IAssistantEngine? CreateDefaultEngine(ISettingsStore settingsStore)
    {
        if (AssistantEngineFactory is not null)
        {
            return AssistantEngineFactory(settingsStore);
        }

        if (LanguageModelFactory is null)
        {
            return null;
        }

        var model = LanguageModelFactory();
        var audio = AudioCaptureFactory?.Invoke();
        var stt = SpeechToTextFactory?.Invoke();
        var spotter = KeywordSpotterFactory?.Invoke();
        var player = SoundPlayerFactory?.Invoke();
        var tts = TextToSpeechFactory?.Invoke();
        var translator = TranslatorFactory?.Invoke() ?? new LlmTranslator(model);

        return new AssistantEngine(
            settingsStore,
            model,
            audio,
            stt,
            spotter,
            tts,
            player,
            translator);
    }
}