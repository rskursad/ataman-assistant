using Ataman.Core.Model;

namespace Ataman.Core.Translate;

/// <summary>
/// Local, on-device translation engine using the loaded <see cref="ILanguageModel"/>.
/// Translates input prompts from any language to English (for superior LLM reasoning),
/// and can translate responses back to the user's conversation language.
/// </summary>
public sealed class LlmTranslator : ITranslator
{
    private readonly ILanguageModel _model;
    private TranslationLanguage _source;
    private TranslationLanguage _target;

    public LlmTranslator(
        ILanguageModel model,
        TranslationLanguage? source = null,
        TranslationLanguage? target = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _source = source ?? new TranslationLanguage("auto", "Auto");
        _target = target ?? new TranslationLanguage("en", "English");
    }

    public TranslationLanguage Source => _source;
    public TranslationLanguage Target => _target;

    public void SetDirection(TranslationLanguage source, TranslationLanguage target)
    {
        _source = source;
        _target = target;
    }

    /// <summary>
    /// Translates text using the configured Source and Target languages.
    /// </summary>
    public Task<string> TranslateAsync(string text, CancellationToken ct = default) =>
        TranslateAsync(text, _source.DisplayName, _target.DisplayName, ct);

    /// <summary>
    /// Translates text between specified languages. Fast-paths identical languages
    /// or empty input without calling the model.
    /// </summary>
    public async Task<string> TranslateAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken ct = default)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        // Fast-path: no translation needed if source matches target or if already English when targeting English
        if (sourceLang.Equals(targetLang, StringComparison.OrdinalIgnoreCase) ||
            (targetLang.Equals("en", StringComparison.OrdinalIgnoreCase) &&
             sourceLang.Equals("en", StringComparison.OrdinalIgnoreCase)))
        {
            return trimmed;
        }

        if (!_model.IsLoaded)
        {
            // If model is not loaded yet, return original text rather than failing
            return trimmed;
        }

        var systemPrompt =
            $"You are an expert translator. Translate the text into {targetLang}. " +
            "Output ONLY the direct translation. Do not include notes, comments, or explanations.";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, trimmed),
        };

        // Greedy decoding (temp = 0.1) for maximum speed and deterministic translation
        var maxTokens = Math.Clamp(trimmed.Length * 2, 64, 512);
        var options = new GenerationOptions(Temperature: 0.1f, MaxTokens: maxTokens);

        var translated = await _model.CompleteAsync(messages, options, ct).ConfigureAwait(false);
        return CleanTranslationOutput(translated, trimmed);
    }

    private static string CleanTranslationOutput(string output, string fallback)
    {
        var clean = output.Trim();

        // Strip leading common prefixes like "Translation: " or "Output: "
        if (clean.StartsWith("Translation:", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean["Translation:".Length..].Trim();
        }
        else if (clean.StartsWith("Output:", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean["Output:".Length..].Trim();
        }

        // Strip enclosing quotes if model wrapped the whole translation in quotes
        if (clean.Length >= 2 && clean.StartsWith('"') && clean.EndsWith('"'))
        {
            clean = clean[1..^1].Trim();
        }

        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }
}
