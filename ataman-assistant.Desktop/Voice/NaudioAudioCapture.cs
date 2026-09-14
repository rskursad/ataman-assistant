using Ataman.Core.Voice;
using NAudio.Wave;

namespace AtamanAssistant.Desktop.Voice;

/// <summary>
/// Desktop microphone capture via NAudio (WaveInEvent), resampling to the
/// canonical 16 kHz mono s16le stream.
/// </summary>
public sealed class NaudioAudioCapture : IAudioCapture, IDisposable
{
    private readonly object _gate = new();
    private WaveInEvent? _waveIn;
    private CancellationTokenSource? _cts;

    public event EventHandler<AudioFrameEventArgs>? AudioFrameReceived;
    public event EventHandler<CaptureState>? StateChanged;

    public Task StartAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_waveIn is not null)
            {
                return Task.CompletedTask;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(AudioFormat.SampleRate, AudioFormat.BitsPerSample, AudioFormat.Channels),
                BufferMilliseconds = 100,
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            try
            {
                _waveIn.StartRecording();
            }
            catch (Exception)
            {
                _waveIn.Dispose();
                _waveIn = null;
                throw;
            }
        }

        StateChanged?.Invoke(this, CaptureState.Listening);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _waveIn?.StopRecording();
        }

        StateChanged?.Invoke(this, CaptureState.Stopped);
        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0)
        {
            return;
        }

        var frame = new AudioFrameEventArgs(
            new ArraySegment<byte>(e.Buffer, 0, e.BytesRecorded),
            Environment.TickCount64);
        AudioFrameReceived?.Invoke(this, frame);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_gate)
        {
            _waveIn?.Dispose();
            _waveIn = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _waveIn?.Dispose();
            _waveIn = null;
        }
    }
}