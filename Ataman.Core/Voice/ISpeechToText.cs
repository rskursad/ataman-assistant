namespace Ataman.Core.Voice;

/// <summary>A final (or partial) recognition produced by the STT engine.</summary>
public sealed record FinalRecognition(string Text, bool IsFinal);

/// <summary>
/// Offline, streaming speech-to-text. Frames are pushed in the canonical
/// 16 kHz mono s16le format and results are raised asynchronously.
/// </summary>
public interface ISpeechToText
{
    event EventHandler<string>? PartialResult;
    event EventHandler<FinalRecognition>? Recognized;

    Task LoadModelAsync(string modelPath, CancellationToken ct = default);
    void Feed(ReadOnlyMemory<byte> pcmFrames);
    void Reset();
}