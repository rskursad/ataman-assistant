namespace Ataman.Core.Voice;

/// <summary>
/// Generates synthetic PCM audio tones (e.g. wake-word chime, success, error)
/// without requiring external media files.
/// </summary>
public static class ToneGenerator
{
    /// <summary>
    /// Generates a pleasant two-tone rising chime (e.g. 587 Hz -> 880 Hz)
    /// suitable for wake-word activation notification.
    /// </summary>
    public static byte[] CreateWakeChime(int sampleRate = AudioFormat.SampleRate)
    {
        // 2 notes: Tone 1 (D5 ~587Hz) for 80ms, Tone 2 (A5 ~880Hz) for 120ms
        const double freq1 = 587.33;
        const double freq2 = 880.00;
        var dur1 = (int)(sampleRate * 0.080);
        var dur2 = (int)(sampleRate * 0.120);
        var totalSamples = dur1 + dur2;
        var pcm = new byte[totalSamples * 2];

        // Tone 1
        for (var i = 0; i < dur1; i++)
        {
            var t = (double)i / sampleRate;
            var envelope = Math.Sin(Math.PI * i / dur1) * Math.Exp(-2.5 * i / dur1);
            var sample = (short)(Math.Sin(2 * Math.PI * freq1 * t) * envelope * 12000);
            var idx = i * 2;
            pcm[idx] = (byte)(sample & 0xFF);
            pcm[idx + 1] = (byte)((sample >> 8) & 0xFF);
        }

        // Tone 2
        for (var i = 0; i < dur2; i++)
        {
            var t = (double)i / sampleRate;
            var envelope = Math.Sin(Math.PI * i / dur2) * Math.Exp(-1.5 * i / dur2);
            var sample = (short)(Math.Sin(2 * Math.PI * freq2 * t) * envelope * 14000);
            var idx = (dur1 + i) * 2;
            pcm[idx] = (byte)(sample & 0xFF);
            pcm[idx + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return pcm;
    }
}
