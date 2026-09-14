using System.Text.Json;
using Ataman.Core;
using Ataman.Core.Settings;
using Ataman.Core.Voice;
using PiperSharp;
using PiperSharp.Models;

namespace AtamanAssistant.Desktop.Voice;

/// <summary>
/// Faz 3: offline Piper TTS on Desktop. Downloads the piper1 (rhasspy/piper)
/// standalone executable and the selected voice (.onnx + config) once into the
/// app data folder, then synthesizes responses and streams the raw PCM into the
/// shared <see cref="ISoundPlayer"/> (NAudio).
/// </summary>
public sealed class PiperTts : ITextToSpeech
{
    private readonly ISoundPlayer _soundPlayer;
    private readonly object _gate = new();

    private TtsVoice _currentVoice = VoiceCatalog.TurkishDfki;
    private VoiceModel? _model;
    private int _sampleRate = 22_050;
    private bool _ready;

    public PiperTts(ISoundPlayer soundPlayer)
    {
        _soundPlayer = soundPlayer;
    }

    public event EventHandler<AudioFrameEventArgs>? AudioFrameProduced;
    public event EventHandler? SpeechStarted;
    public event EventHandler? SpeechCompleted;

    public IReadOnlyList<TtsVoice> AvailableVoices { get; } = VoiceCatalog.All;

    public TtsVoice? CurrentVoice
    {
        get
        {
            lock (_gate)
            {
                return _currentVoice;
            }
        }
    }

    public bool IsSpeaking { get; private set; }

    public Task SetVoiceAsync(TtsVoice voice, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _currentVoice = voice;
            _ready = false;
        }

        return Task.CompletedTask;
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var model = await EnsureReadyAsync(ct).ConfigureAwait(false);

        var provider = new PiperProvider(new PiperConfiguration
        {
            ExecutableLocation = PiperExecutablePath,
            WorkingDirectory = PiperWorkingDirectory,
            Model = model,
            SpeakingRate = 1f,
        });

        SpeechStarted?.Invoke(this, EventArgs.Empty);
        IsSpeaking = true;
        try
        {
            var pcm = await Task.Run(() => provider.InferAsync(text, AudioOutputType.Raw, ct), ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            _soundPlayer.Play(pcm, _sampleRate);
        }
        finally
        {
            IsSpeaking = false;
            SpeechCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task StopSpeakingAsync()
    {
        _soundPlayer.Stop();
        IsSpeaking = false;
        return Task.CompletedTask;
    }

    private string PiperDirectory => Path.Combine(AppPaths.BaseDirectory, "piper");
    private string PiperExecutablePath => Path.Combine(PiperDirectory, PiperDownloader.PiperExecutable);
    private string PiperWorkingDirectory => PiperDirectory;

    /// <summary>
    /// Lazily downloads the piper bundle and the selected voice (once each),
    /// then reuses the parsed <see cref="VoiceModel"/> for inference.
    /// </summary>
    private async Task<VoiceModel> EnsureReadyAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_ready && _model is not null)
            {
                return _model;
            }

            _model = null;
        }

        if (File.Exists(PiperExecutablePath) is false)
        {
            var stream = await PiperDownloader.DownloadPiper().ConfigureAwait(false);
            await Task.Run(() => stream.ExtractPiper(AppPaths.BaseDirectory), ct).ConfigureAwait(false);
        }

        var key = VoiceCatalog.ForId(_currentVoice.Id).Id;
        var modelDir = Path.Combine(AppPaths.TtsVoices, key);
        var modelJson = Path.Combine(modelDir, "model.json");

        VoiceModel? model = null;
        if (File.Exists(modelJson))
        {
            model = await VoiceModel.LoadModel(modelDir).ConfigureAwait(false);
            if (RequiresDownload(model))
            {
                model = null;
            }
        }

        if (model is null)
        {
            var info = await PiperDownloader.GetModelByKey(key).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Ses modeli bulunamadı: {key}");
            await info.DownloadModel(AppPaths.TtsVoices).ConfigureAwait(false);
            model = await VoiceModel.LoadModel(modelDir).ConfigureAwait(false);
        }

        lock (_gate)
        {
            _model = model;
            _currentVoice = VoiceCatalog.ForId(key);
            _sampleRate = ReadSampleRate(model);
            _ready = true;
        }

        return model;
    }

    /// <summary>
    /// The piper1 voices.json has no top-level "audio"; the sample rate lives in
    /// each voice's .onnx.json config. Defaults to 22050 (every published Piper
    /// voice) when absent.
    /// </summary>
    private static int ReadSampleRate(VoiceModel model)
    {
        var onnxJson = model.Files?.Keys.FirstOrDefault(f => f.EndsWith(".onnx.json", StringComparison.OrdinalIgnoreCase));
        if (model.ModelLocation is not null && onnxJson is not null)
        {
            var path = Path.Combine(model.ModelLocation, Path.GetFileName(onnxJson));
            if (File.Exists(path))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
                    if (doc.RootElement.TryGetProperty("audio", out var audio)
                        && audio.TryGetProperty("sample_rate", out var rate))
                    {
                        return rate.GetInt32();
                    }
                }
                catch
                {
                    // Fall back below on malformed config.
                }
            }
        }

        return 22_050;
    }

    private static bool RequiresDownload(VoiceModel model)
    {
        if (model.Files is not { Count: > 0 })
        {
            return true;
        }

        var onnxFile = Path.GetFileName(model.Files.Keys.FirstOrDefault(f => f.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)));
        if (model.ModelLocation is null || string.IsNullOrWhiteSpace(onnxFile))
        {
            return true;
        }

        return File.Exists(Path.Combine(model.ModelLocation, onnxFile)) is false;
    }
}