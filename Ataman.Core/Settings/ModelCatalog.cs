namespace Ataman.Core.Settings;

/// <summary>Metadata for a downloadable GGUF model.</summary>
public sealed record ModelInfo(
    string Id,
    string DisplayName,
    long Bytes,
    string DownloadUrl,
    int RecommendedContextTokens = 2048,
    string? ChatTemplate = null);

/// <summary>Catalog of supported on-device models (Q4_K_M build for phones).</summary>
public static class ModelCatalog
{
    public static ModelInfo Qwen25_0_5B { get; } = new(
        Id: "qwen2.5-0.5b-instruct-q4_k_m",
        DisplayName: "Qwen2.5-0.5B-Instruct (Q4_K_M) - en hafif",
        Bytes: 400_000_000,
        DownloadUrl: "https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/qwen2.5-0.5b-instruct-q4_k_m.gguf",
        RecommendedContextTokens: 1024);

    public static ModelInfo Qwen25_1_5B { get; } = new(
        Id: "qwen2.5-1.5b-instruct-q4_k_m",
        DisplayName: "Qwen2.5-1.5B-Instruct (Q4_K_M) - tavsiye",
        Bytes: 1_060_000_000,
        DownloadUrl: "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf",
        RecommendedContextTokens: 2048);

    public static ModelInfo Llama_3_2_1B { get; } = new(
        Id: "llama-3.2-1b-instruct-q4_k_m",
        DisplayName: "Llama-3.2-1B-Instruct (Q4_K_M) - en hızlı",
        Bytes: 810_000_000,
        DownloadUrl: "https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF/resolve/main/Llama-3.2-1B-Instruct-Q4_K_M.gguf",
        RecommendedContextTokens: 2048);

    public static ModelInfo Llama_3_2_3B { get; } = new(
        Id: "llama-3.2-3b-instruct-q4_k_m",
        DisplayName: "Llama-3.2-3B-Instruct (Q4_K_M) - amiral telefonlar",
        Bytes: 2_000_000_000,
        DownloadUrl: "https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF/resolve/main/Llama-3.2-3B-Instruct-Q4_K_M.gguf",
        RecommendedContextTokens: 2048);

    public static IReadOnlyList<ModelInfo> All { get; } = new[] { Qwen25_0_5B, Qwen25_1_5B, Llama_3_2_1B, Llama_3_2_3B };

    public static ModelInfo ForId(string? id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Qwen25_1_5B;
}