using Android.App;
using Android.Runtime;
using Ataman.Core.Model;
using Ataman.Core.Voice;
using Avalonia;
using Avalonia.Android;
using AtamanAssistant;
using AtamanAssistant.Android.Audio;
using AtamanAssistant.Android.Voice;

namespace AtamanAssistant.Android;

[Application]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    public MainApplication(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();

        AppServices.LanguageModelFactory = () => new LlamaDotNetModel();
        AppServices.SpeechToTextFactory = () => new VoskSpeechToText();
        AppServices.KeywordSpotterFactory = () => new VoskKeywordSpotter();
        AppServices.AudioCaptureFactory = () => new AndroidAudioSource();
        AppServices.TextToSpeechFactory = () => new AndroidTextToSpeech(this);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}