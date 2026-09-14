using System;
using Avalonia;
using Ataman.Core.Model;
using Ataman.Core.Voice;
using AtamanAssistant.Desktop.Voice;

namespace AtamanAssistant.Desktop;

internal sealed class Program
{
    // Initialize Avalonia and configure it's platform.
    [STAThread]
    public static void Main(string[] args)
    {
        var soundPlayer = new NaudioSoundPlayer();
        AppServices.LanguageModelFactory = () => new LLamaSharpModel();
        AppServices.SpeechToTextFactory = () => new VoskSpeechToText();
        AppServices.KeywordSpotterFactory = () => new VoskKeywordSpotter();
        AppServices.AudioCaptureFactory = () => new NaudioAudioCapture();
        AppServices.SoundPlayerFactory = () => soundPlayer;
        AppServices.TextToSpeechFactory = () => new PiperTts(soundPlayer);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}