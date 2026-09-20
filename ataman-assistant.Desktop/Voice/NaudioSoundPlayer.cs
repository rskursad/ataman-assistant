using Ataman.Core.Voice;
using NAudio.Wave;

namespace AtamanAssistant.Desktop.Voice;

/// <summary>
/// Mono s16le PCM playback through NAudio WaveOutEvent; used by the Piper TTS
/// engine and keyword chimes.
/// </summary>
public sealed class NaudioSoundPlayer : ISoundPlayer, IDisposable
{
    private readonly object _gate = new();
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _buffer;
    private int _currentSampleRate;

    public void Play(ReadOnlyMemory<byte> pcm, int sampleRate)
    {
        if (pcm.IsEmpty)
        {
            return;
        }

        lock (_gate)
        {
            EnsureWaveOut(sampleRate);
            _buffer!.AddSamples(pcm.ToArray(), 0, pcm.Length);
        }
    }

    public async Task PlayAsync(ReadOnlyMemory<byte> pcm, int sampleRate, CancellationToken ct = default)
    {
        if (pcm.IsEmpty)
        {
            return;
        }

        Play(pcm, sampleRate);

        while (!ct.IsCancellationRequested)
        {
            lock (_gate)
            {
                if (_buffer is null || _buffer.BufferedBytes == 0)
                {
                    break;
                }
            }

            await Task.Delay(30, ct).ConfigureAwait(false);
        }

        // Allow WaveOut buffer latency (approx 150-200ms) to flush to speakers
        try
        {
            await Task.Delay(150, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation during final flush
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _buffer?.ClearBuffer();
            _waveOut?.Stop();
        }
    }

    private void EnsureWaveOut(int sampleRate)
    {
        if (_waveOut is not null && _currentSampleRate == sampleRate)
        {
            return;
        }

        TeardownWaveOut();

        _currentSampleRate = sampleRate;
        _buffer = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1))
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMinutes(2),
        };

        _waveOut = new WaveOutEvent { DesiredLatency = 150 };
        _waveOut.Init(_buffer);
        _waveOut.Play();
    }

    private void TeardownWaveOut()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        _buffer = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            TeardownWaveOut();
        }
    }
}