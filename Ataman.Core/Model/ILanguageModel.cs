namespace Ataman.Core.Model;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool,
}

/// <summary>A single chat message in the model conversation.</summary>
public sealed record ChatMessage(ChatRole Role, string Content);

public enum GrammarMode
{
    /// <summary>Free-form text generation.</summary>
    None,
    /// <summary>Constrain output to valid JSON (object or array).</summary>
    Json,
    /// <summary>Constrain output to a JSON object matching a schema.</summary>
    JsonSchema,
    /// <summary>Constrain output via a raw GBNF grammar.</summary>
    Custom,
}

public sealed record GenerationOptions(
    float Temperature = 0.7f,
    int MaxTokens = 192,
    int? TopK = null,
    float? TopP = null,
    GrammarMode Grammar = GrammarMode.None,
    string? GrammarText = null)
{
    public static GenerationOptions Default { get; } = new();
}

/// <summary>A loaded model file (GGUF) and its runtime configuration.</summary>
public sealed record ModelSpec(
    string Id,
    string FilePath,
    int ContextTokens = 2048,
    int GpuLayers = 0,
    int Threads = 4);

public sealed record ModelLoadReport(string Id, long Bytes, double TokensPerSecond);

/// <summary>A tool the model may call.</summary>
public sealed record ToolDefinition(string Name, string Description, string? ParametersSchemaJson);

/// <summary>A structured tool invocation produced by the model.</summary>
public sealed record ToolCall(string Name, string ArgumentsJson, string RawText);

/// <summary>The result of executing a tool call.</summary>
public sealed record ToolResult(string Name, string ResultJson, bool Success, string? Error);

/// <summary>
/// On-device LLM executor. Implementations wrap llama.cpp (LLamaSharp or a
/// thin P/Invoke layer) and expose grammar-constrained completion, which is
/// what makes small models emit reliable tool calls.
/// </summary>
public interface ILanguageModel : IAsyncDisposable
{
    bool IsLoaded { get; }

    event EventHandler<string>? TokenProduced;
    event EventHandler<ToolCall>? ToolCallProduced;

    Task<ModelLoadReport> LoadAsync(ModelSpec spec, CancellationToken ct = default);
    Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options = default,
        CancellationToken ct = default);

    Task<ToolCall?> CompleteToolCallAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct = default);

    Task UnloadAsync();
}