namespace Ataman.Core.Voice;

/// <summary>
/// Lightweight always-on wake-word detection. Consumes the same PCM stream as
/// ISpeechToText and raises <see cref="WakeWordDetected"/> on a phrase match.
/// </summary>
public interface IKeywordSpotter
{
    event EventHandler? WakeWordDetected;

    Task LoadAsync(string modelPath, CancellationToken ct = default);
    /// <summary>Restrict spotting to the given phrases (grammar).</summary>
    void SetKeywords(IEnumerable<string> keywords);
    void Feed(ReadOnlyMemory<byte> pcmFrames);
    void Reset();
}