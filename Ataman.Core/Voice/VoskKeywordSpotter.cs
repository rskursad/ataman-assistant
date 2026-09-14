using System.Text.Json;

namespace Ataman.Core.Voice;

/// <summary>
/// Always-on wake-word detection through a Vosk grammar recognizer. The
/// grammar (a plain JSON array of phrases) restricts decoding to the
/// configured keywords, so a final result is a wake-word match. Reuses the
/// same Vosk model as the STT engine.
/// </summary>
public sealed class VoskKeywordSpotter : IKeywordSpotter
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private Vosk.Model? _model;
    private Vosk.VoskRecognizer? _recognizer;
    private string? _grammarJson;

    public event EventHandler? WakeWordDetected;

    public async Task LoadAsync(string modelPath, CancellationToken ct = default)
    {
        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Vosk.Vosk.SetLogLevel(0);
            _model ??= new Vosk.Model(modelPath);
            Reset();
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public void SetKeywords(IEnumerable<string> keywords)
    {
        var list = keywords.Select(k => k.Trim()).Where(k => k.Length > 0).ToList();
        _grammarJson = JsonSerializer.Serialize(list);
        Reset();
    }

    public void Reset()
    {
        lock (_gate)
        {
            _recognizer?.Dispose();
            _recognizer = _model is null || _grammarJson is null
                ? null
                : new Vosk.VoskRecognizer(_model, AudioFormat.SampleRate, _grammarJson);
        }
    }

    public void Feed(ReadOnlyMemory<byte> pcmFrames)
    {
        if (pcmFrames.IsEmpty)
        {
            return;
        }

        Vosk.VoskRecognizer? recognizer;
        lock (_gate)
        {
            recognizer = _recognizer;
        }

        if (recognizer is null)
        {
            return;
        }

        var bytes = pcmFrames.ToArray();
        lock (_gate)
        {
            if (recognizer.AcceptWaveform(bytes, bytes.Length))
            {
                var text = VoskSpeechToText.ExtractText(recognizer.FinalResult());
                if (text.Length > 0)
                {
                    WakeWordDetected?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }
}