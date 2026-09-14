using Ataman.Core.Voice;

namespace Ataman.Core.Settings;

/// <summary>Static catalog of selectable open-source TTS voices (Piper, piper1 layout).</summary>
public static class VoiceCatalog
{
    public static TtsVoice TurkishDfki { get; } = new("tr_TR-dfki-medium", "DFKI (TR)", "tr", "M");
    public static TtsVoice EnglishLessac { get; } = new("en_US-lessac-medium", "Lessac (EN-US)", "en", "F");
    public static TtsVoice EnglishLessacLow { get; } = new("en_US-lessac-low", "Lessac low (EN-US)", "en", "F");
    public static TtsVoice EnglishRyan { get; } = new("en_US-ryan-high", "Ryan (EN-US)", "en", "M");

    public static IReadOnlyList<TtsVoice> All { get; } = new[]
    {
        TurkishDfki,
        EnglishLessac,
        EnglishLessacLow,
        EnglishRyan,
    };

    public static TtsVoice ForId(string? id) =>
        All.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase)) ?? TurkishDfki;
}