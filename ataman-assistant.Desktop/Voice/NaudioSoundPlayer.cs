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

    public void Play(ReadOnlyMemory<byte> pcm, int sampleRate)
    {
        lock (_gate)
        {
            EnsureWaveOut(sampleRate);
            _buffer!.AddSamples(pcm.ToArray(), 0, pcm.Length);
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
        if (_waveOut is not null)
        {
            return;
        }

        _buffer = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1))
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(2000),
        };

        _waveOut = new WaveOutEvent { DesiredLatency = 200 };
        _waveOut.Init(_buffer);
        _waveOut.Play();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _waveOut?.Dispose();
            _waveOut = null;
            _buffer = null;
        }
    }
}