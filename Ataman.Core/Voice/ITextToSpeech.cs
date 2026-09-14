namespace Ataman.Core.Voice;

/// <summary>A selectable TTS voice pack (open-source voice, e.g. Piper).</summary>
public sealed record TtsVoice(string Id, string DisplayName, string Language, string Gender);

/// <summary>
/// Offline text-to-speech. Generated samples are streamed through
/// <see cref="AudioFrameProduced"/> for the platform sound player to consume.
/// </summary>
public interface ITextToSpeech
{
    event EventHandler<AudioFrameEventArgs>? AudioFrameProduced;
    event EventHandler? SpeechStarted;
    event EventHandler? SpeechCompleted;

    IReadOnlyList<TtsVoice> AvailableVoices { get; }
    TtsVoice? CurrentVoice { get; }
    bool IsSpeaking { get; }

    Task SetVoiceAsync(TtsVoice voice, CancellationToken ct = default);
    Task SpeakAsync(string text, CancellationToken ct = default);
    Task StopSpeakingAsync();
}

/// <summary>Plays raw PCM audio on the host platform.</summary>
public interface ISoundPlayer
{
    void Play(ReadOnlyMemory<byte> pcm, int sampleRate);
    void Stop();
}