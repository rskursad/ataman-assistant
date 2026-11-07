using System.Text;

namespace Ataman.Core.Model;

/// <summary>
/// Single-source chat template formatter for the supported GGUF families.
/// Mirrors the official Qwen2.x and Llama-3.x chat formats so generation is
/// stable across executors (LLamaSharp, jllama). The <c>Tool</c> role maps to
/// the model-specific tool answer channel.
/// </summary>
public static class ChatTemplate
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