namespace Ataman.Core.Voice;

/// <summary>
/// Canonical audio format used throughout the voice pipeline:
/// 16 kHz, mono, 16-bit signed little-endian PCM (s16le).
/// </summary>
public static class AudioFormat
{
    public const int SampleRate = 16000;
    public const int Channels = 1;
    public const int BitsPerSample = 16;
}