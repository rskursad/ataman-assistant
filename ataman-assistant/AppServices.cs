using Ataman.Core.Model;
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
}