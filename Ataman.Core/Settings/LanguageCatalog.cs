namespace Ataman.Core.Settings;

/// <summary>Locale metadata used to pick STT models, TTS voices and wake words.</summary>
public sealed record LanguageInfo(
    string Code,
    string DisplayName,
    string VoskModelId,
    string VoskSmallModelId,
    string DefaultTtsVoiceId,
    string DefaultWakeWord);

/// <summary>Static catalog of supported conversation languages.</summary>
public static class LanguageCatalog
{
    public static LanguageInfo Turkish { get; } =
        new("tr", "Türkçe", "vosk-model-small-tr-0.3", "vosk-model-small-tr-0.3", "tr_TR-dfki-medium", "asistan");

    public static LanguageInfo English { get; } =
        new("en", "English", "vosk-model-en-us-0.22", "vosk-model-small-en-us-0.15", "en_US-lessac-medium", "ataman");

    public static IReadOnlyList<LanguageInfo> All { get; } = new[] { Turkish, English };

    public static LanguageInfo ForCode(string? code) =>
        All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)) ?? English;
}