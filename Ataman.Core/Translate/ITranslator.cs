namespace Ataman.Core.Translate;

public sealed record TranslationLanguage(string Code, string DisplayName);

/// <summary>
/// Offline language translation. Direction is fixed by the implementation
/// (e.g. Turkish → English for the LLM, then English → Turkish for TTS).
/// </summary>
public interface ITranslator
{
    TranslationLanguage Source { get; }
    TranslationLanguage Target { get; }

    Task<string> TranslateAsync(string text, CancellationToken ct = default);
}