using System.Text.Json;

namespace Ataman.Core.Voice;

/// <summary>
/// Vosk-powered streaming speech-to-text. Uses the canonical 16 kHz mono
/// s16le PCM stream. On Windows the Vosk NuGet supplies libvosk.dll; on
/// Android libvosk.so is shipped via the AndroidNativeLibrary item.
/// </summary>
public sealed class VoskSpeechToText : ISpeechToText
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private Vosk.Model? _model;
    private Vosk.VoskRecognizer? _recognizer;
    private string? _loadedModelPath;

    public event EventHandler<string>? PartialResult;
    public event EventHandler<FinalRecognition>? Recognized;

    public async Task LoadModelAsync(string modelPath, CancellationToken ct = default)
    {
        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_model is not null && string.Equals(_loadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Vosk.Vosk.SetLogLevel(0);
            lock (_gate)
            {
                _recognizer?.Dispose();
                _recognizer = null;
                _model?.Dispose();
                _model = new Vosk.Model(modelPath);
                _loadedModelPath = modelPath;
            }

            Reset();
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _recognizer?.Dispose();
            _recognizer = _model is null ? null : new Vosk.VoskRecognizer(_model, AudioFormat.SampleRate);
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
                var final = ExtractText(recognizer.FinalResult());
                if (final.Length > 0)
                {
                    Recognized?.Invoke(this, new FinalRecognition(final, IsFinal: true));
                }
            }
            else
            {
                var partial = ExtractText(recognizer.PartialResult());
                if (partial.Length > 0)
                {
                    PartialResult?.Invoke(this, partial);
                }
            }
        }
    }

    public static string ExtractText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }
}