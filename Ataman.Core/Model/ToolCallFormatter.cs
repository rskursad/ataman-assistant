using System.Text.Json;

namespace Ataman.Core.Model;

/// <summary>
/// Shared heuristic tool-call prompt/parse logic used by every
/// <see cref="ILanguageModel"/> implementation. Small on-device models are poor
/// at emitting strict JSON, so the prompt is kept explicit and the parser
/// tolerates ```json fences and surrounding prose.
/// </summary>
public static class ToolCallFormatter
{
    /// <summary>Serializes tools for prompt injection.</summary>
    public static string SerializeTools(IReadOnlyList<ToolDefinition> tools) =>
        JsonSerializer.Serialize(tools.Select(t => new
        {
            t.Name,
            t.Description,
            parameters = t.ParametersSchemaJson,
        }));

    /// <summary>Appends the tool-calling instruction as a synthetic user turn.</summary>
    public static IReadOnlyList<ChatMessage> WithToolInstructions(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools)
    {
        var instructions = new List<ChatMessage>(messages)
        {
            new(ChatRole.User,
                "Available tools (JSON array): " + SerializeTools(tools) + "\n" +
                "If you must call a tool, answer ONLY with a JSON object: " +
                "{\"name\": \"tool_name\", \"arguments\": {...}}. Otherwise answer normally."),
        };

        return instructions;
    }

    /// <summary>
    /// Parses model output into a <see cref="ToolCall"/>. Returns
    /// <c>null</c> when the output is not a tool call (normal answer).
    /// </summary>
    public static ToolCall? Parse(string rawText)
    {
        var text = rawText;
        TrimToolWrapper(ref text);

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("name", out var nameEl))
            {
                return null;
            }

            var name = nameEl.GetString();
            var args = doc.RootElement.TryGetProperty("arguments", out var argsEl)
                ? argsEl.GetRawText()
                : "{}";
            if (!string.IsNullOrWhiteSpace(name))
            {
                return new ToolCall(name, args, text);
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static void TrimToolWrapper(ref string text)
    {
        text = text.Trim();
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            text = text[7..];
        }
        else if (text.StartsWith("```"))
        {
            text = text[3..];
        }

        if (text.EndsWith("```", StringComparison.Ordinal))
        {
            text = text[..^3];
        }

        text = text.Trim();
    }
}