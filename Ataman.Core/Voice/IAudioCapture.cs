namespace Ataman.Core.Voice;

public sealed class AudioFrameEventArgs(ArraySegment<byte> pcm, long timestampMs) : EventArgs
{
    /// <summary>Raw s16le PCM at 16 kHz mono.</summary>
    public ReadOnlyMemory<byte> Pcm { get; } = pcm;

    /// <summary>Monotonic capture timestamp in milliseconds.</summary>
    public long TimestampMs { get; } = timestampMs;
}

public interface IAudioCapture
{
    event EventHandler<AudioFrameEventArgs>? AudioFrameReceived;
    event EventHandler<CaptureState>? StateChanged;

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
}

public enum CaptureState
{
    Stopped,
    Listening,
}