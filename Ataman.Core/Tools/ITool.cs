using Ataman.Core.Model;

namespace Ataman.Core.Tools;

/// <summary>
/// A capability the assistant can offer the model. Tools run fully on-device;
/// the model decides when to call them via <see cref="ILanguageModel.CompleteToolCallAsync"/>.
/// </summary>
public interface ITool
{
    /// <summary>Stable machine name the model emits in {"name": ...}.</summary>
    string Name { get; }

    /// <summary>Natural-language description the model sees.</summary>
    string Description { get; }

    /// <summary>Optional JSON-schema text describing the arguments object.</summary>
    string? ParametersSchemaJson { get; }

    Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default);
}