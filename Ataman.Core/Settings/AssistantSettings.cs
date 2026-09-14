using System.Text.Json;

namespace Ataman.Core.Settings;

/// <summary>Persisted runtime settings of the assistant.</summary>
public sealed class AssistantSettings
{
    public string ConversationLanguage { get; set; } = "tr";

    /// <summary>If true, spoken text is translated to English before the LLM.</summary>
    public bool TranslateToEnglish { get; set; } = true;

    public string WakeWord { get; set; } = "asistan";
    public bool AlwaysListening { get; set; } = true;

    public string TtsVoiceId { get; set; } = "tr_TR-dfki-medium";

    /// <summary>If true, assistant responses are spoken with the TTS engine.</summary>
    public bool SpeakResponses { get; set; } = true;

    /// <summary>Model id from <see cref="ModelCatalog"/>, e.g. "qwen2.5-1.5b-instruct-q4_k_m".</summary>
    public string ModelId { get; set; } = "qwen2.5-1.5b-instruct-q4_k_m";

    /// <summary>Absolute path of the downloaded GGUF file, if any.</summary>
    public string? ModelFilePath { get; set; }

    public int MaxResponseTokens { get; set; } = 160;
    public float Temperature { get; set; } = 0.7f;

    /// <summary>Optional hot-loaded fine-tune adapter (LoRA) path.</summary>
    public string? LoraAdapterPath { get; set; }

    public bool NotifyOnWake { get; set; } = true;
}