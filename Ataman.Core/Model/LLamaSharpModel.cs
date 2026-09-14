using System.Text;
using System.Text.Json;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace Ataman.Core.Model;

/// <summary>
/// llama.cpp executor via LLamaSharp. Applies the model chat template by hand
/// (Qwen2.x / Llama-3.x) so generation is stable across LLamaSharp versions,
/// and streams decoded tokens via <see cref="TokenProduced"/>. Tool calls are
/// parsed heuristically from the raw text for Phase 2; strict JSON grammar
/// constraints arrive with the tool loop.
/// </summary>
public sealed class LLamaSharpModel : ILanguageModel
{
    private readonly object _gate = new();
    private LLamaWeights? _weights;
    private StatelessExecutor? _executor;
    private ModelSpec? _spec;
    private bool _disposed;

    public bool IsLoaded => _executor is not null;

    public event EventHandler<string>? TokenProduced;
    public event EventHandler<ToolCall>? ToolCallProduced;

    public Task<ModelLoadReport> LoadAsync(ModelSpec spec, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_executor is not null && _spec?.FilePath == spec.FilePath)
            {
                return Task.FromResult(new ModelLoadReport(spec.Id, new FileInfo(spec.FilePath).Length, 0));
            }

            UnloadLocked();

            var modelParams = new ModelParams(spec.FilePath)
            {
                ContextSize = spec.ContextTokens > 0 ? (uint)spec.ContextTokens : 2048u,
                GpuLayerCount = spec.GpuLayers,
                Threads = spec.Threads > 0 ? spec.Threads : Environment.ProcessorCount,
            };

            var weights = LLamaWeights.LoadFromFile(modelParams);
            _weights = weights;
            _executor = new StatelessExecutor(weights, modelParams);
            _spec = spec;

            return Task.FromResult(new ModelLoadReport(spec.Id, new FileInfo(spec.FilePath).Length, 0));
        }
    }

    public async Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        GenerationOptions? options,
        CancellationToken ct = default)
    {
        var opts = options ?? GenerationOptions.Default;
        StatelessExecutor? executor = null;
        ModelSpec? spec = null;
        lock (_gate)
        {
            executor = _executor;
            spec = _spec;
        }

        if (executor is null || spec is null)
        {
            throw new InvalidOperationException("Model is not loaded.");
        }

        var prompt = ChatTemplate.Apply(spec.Id, messages);

        var sampling = new DefaultSamplingPipeline
        {
            Temperature = opts.Temperature,
            TopK = opts.TopK ?? 40,
            TopP = opts.TopP ?? 0.95f,
        };

        var inferenceParams = new InferenceParams
        {
            MaxTokens = opts.MaxTokens,
            AntiPrompts = new List<string> { ChatTemplate.EndToken(spec.Id) },
            SamplingPipeline = sampling,
        };

        var sb = new StringBuilder();
        await foreach (var token in executor.InferAsync(prompt, inferenceParams, ct).ConfigureAwait(false))
        {
            sb.Append(token);
            TokenProduced?.Invoke(this, token);
        }

        return sb.ToString().Trim();
    }

    public async Task<ToolCall?> CompleteToolCallAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct = default)
    {
        // Phase 2: heuristic JSON tool-call parsing. A strict JSON-schema
        // grammar replaces this in the tool-loop milestone so small models
        // emit well-formed calls.
        var toolsJson = JsonSerializer.Serialize(tools.Select(t => new
        {
            t.Name,
            t.Description,
            parameters = t.ParametersSchemaJson,
        }));
        var instructions = new List<ChatMessage>(messages)
        {
            new(ChatRole.User,
                "Available tools (JSON array): " + toolsJson + "\n" +
                "If you must call a tool, answer ONLY with a JSON object: " +
                "{\"name\": \"tool_name\", \"arguments\": {...}}. Otherwise answer normally."),
        };

        var text = await CompleteAsync(instructions, new GenerationOptions(Temperature: 0.1f, MaxTokens: 256), ct).ConfigureAwait(false);
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
                var call = new ToolCall(name, args, text);
                ToolCallProduced?.Invoke(this, call);
                return call;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    public Task UnloadAsync()
    {
        lock (_gate)
        {
            UnloadLocked();
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            UnloadLocked();
        }

        return ValueTask.CompletedTask;
    }

    private void UnloadLocked()
    {
        _executor = null;
        _weights?.Dispose();
        _weights = null;
        _spec = null;
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

    private static class ChatTemplate
    {
        public static string Apply(string modelId, IReadOnlyList<ChatMessage> messages)
        {
            if (IsQwen(modelId))
            {
                var sb = new StringBuilder();
                foreach (var m in messages)
                {
                    var tag = m.Role switch
                    {
                        ChatRole.System => "<|im_start|>system",
                        ChatRole.User => "<|im_start|>user",
                        ChatRole.Tool => "<|im_start|>tool",
                        _ => "<|im_start|>assistant",
                    };
                    sb.Append(tag).Append('\n').Append(m.Content).Append("<|im_end|>\n");
                }

                sb.Append("<|im_start|>assistant\n");
                return sb.ToString();
            }

            // Llama-3.x (and other) chat format.
            var sb3 = new StringBuilder("<|begin_of_text|>");
            foreach (var m in messages)
            {
                var tag = m.Role switch
                {
                    ChatRole.System => "system",
                    ChatRole.User => "user",
                    ChatRole.Tool => "ipython",
                    _ => "assistant",
                };
                sb3.Append("<|start_header_id|>").Append(tag).Append("<|end_header_id|>\n\n")
                   .Append(m.Content).Append("<|eot_id|>");
            }

            sb3.Append("<|start_header_id|>assistant<|end_header_id|>\n\n");
            return sb3.ToString();
        }

        public static string EndToken(string modelId) => IsQwen(modelId) ? "<|im_end|>" : "<|eot_id|>";

        private static bool IsQwen(string modelId) =>
            modelId.Contains("qwen", StringComparison.OrdinalIgnoreCase);
    }
}