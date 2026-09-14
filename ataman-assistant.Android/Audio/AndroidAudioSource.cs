using Android.Media;
using Ataman.Core.Voice;

namespace AtamanAssistant.Android.Audio;

/// <summary>
/// Mic capture via <see cref="AudioRecord"/>: 16 kHz, mono, 16-bit signed PCM —
/// the canonical frame format expected by <see cref="ISpeechToText"/>. A
/// dedicated thread pumps blocking reads and raises <see cref="AudioFrameEventArgs"/>
/// without touching the UI thread. Requires RECORD_AUDIO permission (requested at
/// startup by <see cref="MainActivity"/>).
/// </summary>
public sealed class AndroidAudioSource : IAudioCapture
{
    private const int SampleRate = 16000;

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Thread? _captureThread;
    private AudioRecord? _audioRecord;
    private bool _released;
    private System.Diagnostics.Stopwatch? _clock;

    public event EventHandler<AudioFrameEventArgs>? AudioFrameReceived;
    public event EventHandler<CaptureState>? StateChanged;

    public Task StartAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_captureThread is not null && _captureThread.IsAlive)
            {
                return Task.CompletedTask;
            }

            var context = global::Android.App.Application.Context;
            if (context.CheckSelfPermission(global::Android.Manifest.Permission.RecordAudio) != global::Android.Content.PM.Permission.Granted)
            {
                return Task.FromException(new UnauthorizedAccessException("Microphone permission not granted."));
            }

            var minBuf = AudioRecord.GetMinBufferSize(
                SampleRate, ChannelIn.Mono, Encoding.Pcm16bit);
            if (minBuf <= 0)
            {
                return Task.FromException(new InvalidOperationException("Could not determine the AudioRecord minimum buffer size."));
            }

            var record = new AudioRecord(
                AudioSource.Mic,
                SampleRate,
                ChannelIn.Mono,
                Encoding.Pcm16bit,
                Math.Max(minBuf * 2, 8192));
            if (record.State != State.Initialized)
            {
                SafeRelease(record);
                return Task.FromException(new InvalidOperationException("AudioRecord could not be initialized."));
            }

            try
            {
                record.StartRecording();
            }
            catch (Exception ex)
            {
                SafeRelease(record);
                return Task.FromException(new InvalidOperationException("Microphone could not be opened.", ex));
            }

            _audioRecord = record;
            _released = false;
            _cts = new CancellationTokenSource();
            _clock = System.Diagnostics.Stopwatch.StartNew();
            _captureThread = new Thread(() => CaptureLoop(record, _cts.Token))
            {
                IsBackground = true,
                Name = "ataman-audio",
            };

            _captureThread.Start();
            StateChanged?.Invoke(this, CaptureState.Listening);
            return Task.CompletedTask;
        }
    }

    public Task StopAsync()
    {
        CancellationTokenSource? cts;
        Thread? thread;
        AudioRecord? record;
        lock (_gate)
        {
            cts = _cts;
            _cts = null;
            thread = _captureThread;
            _captureThread = null;
            record = _audioRecord;
            _audioRecord = null;
            cts?.Cancel();
        }

        if (thread is not null && thread != Thread.CurrentThread && thread.IsAlive)
        {
            _ = thread.Join(TimeSpan.FromSeconds(1));
        }

        lock (_gate)
        {
            SafeStopAndRelease(record);
            cts?.Dispose();
        }

        StateChanged?.Invoke(this, CaptureState.Stopped);
        return Task.CompletedTask;
    }

    private void CaptureLoop(AudioRecord record, CancellationToken token)
    {
        var buffer = new byte[4096];
        try
        {
            while (!token.IsCancellationRequested)
            {
                var count = record.Read(buffer, 0, buffer.Length, (int)AudioRecordReadOptions.Blocking);
                if (count <= 0)
                {
                    continue;
                }

                var ts = _clock?.ElapsedMilliseconds ?? 0;
                AudioFrameReceived?.Invoke(this, new AudioFrameEventArgs(
                    new ArraySegment<byte>(buffer, 0, count), ts));
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            lock (_gate)
            {
                SafeStopAndRelease(record);
            }
        }
    }

    private void SafeStopAndRelease(AudioRecord? record)
    {
        if (_released || record is null)
        {
            return;
        }

        _released = true;
        try
        {
            if (record.RecordingState == RecordState.Recording)
            {
                record.Stop();
            }
        }
        catch (Exception)
        {
        }

        SafeRelease(record);
    }

    private static void SafeRelease(AudioRecord? record)
    {
        if (record is null)
        {
            return;
        }

        try
        {
            record.Release();
        }
        catch (Exception)
        {
        }
    }
}