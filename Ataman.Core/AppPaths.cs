namespace Ataman.Core;

/// <summary>
/// Central location for runtime directories. The host platform sets
/// <see cref="BaseDirectory"/> at startup (e.g. app data dir).
/// </summary>
public static class AppPaths
{
    public static string BaseDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "ataman");

    public static string Models => Path.Combine(BaseDirectory, "models");
    public static string VoskModels => Path.Combine(Models, "vosk");
    public static string LlmModels => Path.Combine(Models, "llm");
    public static string TtsVoices => Path.Combine(Models, "tts");
    public static string Data => Path.Combine(BaseDirectory, "data");
    public static string LoRA => Path.Combine(Models, "lora");

    public static void EnsureCreated()
    {
        foreach (var dir in new[]
        {
            BaseDirectory, Models, VoskModels, LlmModels, TtsVoices, Data, LoRA,
        })
        {
            Directory.CreateDirectory(dir);
        }
    }
}