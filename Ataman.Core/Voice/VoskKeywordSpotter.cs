using System.Text.Json;

namespace Ataman.Core.Voice;

/// <summary>
/// Always-on wake-word detection through a Vosk grammar recognizer with
/// energy-based silence gating for ultra-low idle CPU and power consumption.
/// Words are normalized to lowercase and augmented with phonetic expansions and [unk].
/// </summary>
public sealed class VoskKeywordSpotter : IKeywordSpotter
{
    private const double SilenceThreshold = 300.0; // RMS amplitude below which audio is treated as quiet room
    private readonly object _gate = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private static readonly Dictionary<string, string[]> PhoneticAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // "ataman" is absent from small Vosk dictionaries, but its phonemes /a-t-a-m-a-n/
        // are identical to the dictionary subwords "ata man", "atam an", and "atama" in TR,
        // and "at a man", "auto man", "otto man" in EN.
        ["ataman"] = new[] { "ata man", "atam an", "atama", "adam", "at a man", "auto man", "otto man" },
        ["hey ataman"] = new[] { "hey ata man", "hey atam an", "hey atama" },
        ["alo ataman"] = new[] { "alo ata man", "alo atam an" },
    };

    private Vosk.Model? _model;
    private Vosk.VoskRecognizer? _recognizer;
    private string? _grammarJson;
    private string? _loadedModelPath;
    private readonly List<string> _configuredKeywords = new();

    private byte[]? _preRollBuffer;
    private int _consecutiveActiveFrames;

    public event EventHandler? WakeWordDetected;

    public async Task LoadAsync(string modelPath, CancellationToken ct = default)
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

    public void SetKeywords(IEnumerable<string> keywords)
    {
        var rawList = new List<string>();
        var grammarList = new List<string>();

        foreach (var kw in keywords)
        {
            if (string.IsNullOrWhiteSpace(kw))
            {
                continue;
            }

            // Split comma or semicolon separated keywords if user provided multiple
            var parts = kw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                var normalized = part.Trim().ToLowerInvariant();
                if (normalized.Length > 0 && !rawList.Contains(normalized))
                {
                    rawList.Add(normalized);
                    grammarList.Add(normalized);

                    // Add phonetic expansions if this keyword has known subwords (like "ataman" -> "ata man", "atam an")
                    if (PhoneticAliases.TryGetValue(normalized, out var aliases))
                    {
                        foreach (var alias in aliases)
                        {
                            if (!grammarList.Contains(alias))
                            {
                                grammarList.Add(alias);
                            }
                        }
                    }
                }
            }
        }

        if (rawList.Count == 0)
        {
            rawList.Add("asistan");
            grammarList.Add("asistan");
        }

        // Include [unk] token so Kaldi absorbs background noises instead of falsely triggering
        if (!grammarList.Contains("[unk]"))
        {
            grammarList.Add("[unk]");
        }

        lock (_gate)
        {
            _configuredKeywords.Clear();
            _configuredKeywords.AddRange(rawList);
        }

        _grammarJson = JsonSerializer.Serialize(grammarList);
        Reset();
    }

    public void Reset()
    {
        lock (_gate)
        {
            _recognizer?.Dispose();
            _recognizer = null;
            _preRollBuffer = null;
            _consecutiveActiveFrames = 0;

            if (_model is null || _grammarJson is null)
            {
                return;
            }

            try
            {
                _recognizer = new Vosk.VoskRecognizer(_model, AudioFormat.SampleRate, _grammarJson);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[VoskKeywordSpotter] Grammar recognizer creation failed with custom grammar: {ex.Message}. Falling back to default.");
                try
                {
                    _grammarJson = JsonSerializer.Serialize(new[] { "asistan", "ata man", "atam an", "[unk]" });
                    _recognizer = new Vosk.VoskRecognizer(_model, AudioFormat.SampleRate, _grammarJson);
                }
                catch
                {
                    _recognizer = null;
                }
            }
        }
    }

    public void Feed(ReadOnlyMemory<byte> pcmFrames)
    {
        if (pcmFrames.IsEmpty)
        {
            return;
        }

        // Low-power standby: calculate RMS sound energy
        var rms = CalculateRms(pcmFrames.Span);

        // If the room is quiet, skip the heavy Kaldi neural forward pass!
        // This keeps standby CPU consumption near 0%.
        if (rms < SilenceThreshold && _consecutiveActiveFrames == 0)
        {
            // Keep a 1-frame pre-roll buffer so the start of the wake word is never lost
            _preRollBuffer = pcmFrames.ToArray();
            return;
        }

        if (rms >= SilenceThreshold)
        {
            _consecutiveActiveFrames = Math.Min(30, _consecutiveActiveFrames + 1);
        }
        else
        {
            _consecutiveActiveFrames = Math.Max(0, _consecutiveActiveFrames - 1);
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

        lock (_gate)
        {
            // Feed pre-roll buffer if we just transitioned from silence to sound
            if (_preRollBuffer is not null)
            {
                recognizer.AcceptWaveform(_preRollBuffer, _preRollBuffer.Length);
                _preRollBuffer = null;
            }

            var bytes = pcmFrames.ToArray();
            if (recognizer.AcceptWaveform(bytes, bytes.Length))
            {
                var text = VoskSpeechToText.ExtractText(recognizer.FinalResult()).Trim().ToLowerInvariant();
                if (text.Length > 0 && text != "[unk]")
                {
                    if (IsMatch(text))
                    {
                        WakeWordDetected?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }
    }

    private bool IsMatch(string recognized)
    {
        var cleanRecognized = recognized.Trim().ToLowerInvariant();
        if (cleanRecognized.Length == 0 || cleanRecognized == "[unk]")
        {
            return false;
        }

        var noSpaceRecognized = cleanRecognized.Replace(" ", "");

        lock (_gate)
        {
            foreach (var kw in _configuredKeywords)
            {
                var cleanKw = kw.Trim().ToLowerInvariant();
                var noSpaceKw = cleanKw.Replace(" ", "");

                // Direct match or whitespace-stripped match (e.g. "ata man" -> "ataman")
                if (cleanRecognized == cleanKw || noSpaceRecognized == noSpaceKw)
                {
                    return true;
                }

                // Check configured phonetic aliases
                if (PhoneticAliases.TryGetValue(cleanKw, out var aliases))
                {
                    foreach (var alias in aliases)
                    {
                        if (cleanRecognized == alias || noSpaceRecognized == alias.Replace(" ", ""))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Computes Root Mean Square (RMS) energy of 16-bit mono PCM samples.
    /// Fast O(N) integer arithmetic taking &lt; 5 microseconds per frame.
    /// </summary>
    private static double CalculateRms(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length < 2)
        {
            return 0;
        }

        long sumSquares = 0;
        var sampleCount = pcm.Length / 2;

        for (var i = 0; i < pcm.Length - 1; i += 2)
        {
            short sample = (short)(pcm[i] | (pcm[i + 1] << 8));
            sumSquares += (long)sample * sample;
        }

        return Math.Sqrt((double)sumSquares / sampleCount);
    }
}