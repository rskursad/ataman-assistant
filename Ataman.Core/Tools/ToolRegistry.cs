using Ataman.Core.Model;

namespace Ataman.Core.Tools;

/// <summary>
/// Resolves tools for the model and executes validated tool calls. A tool that
/// is unavailable on the current platform resolves to a graceful
/// <see cref="ToolResult"/> failure instead of throwing.
/// </summary>
public sealed class ToolRegistry
{
    private readonly IReadOnlyDictionary<string, ITool> _tools;

    public IReadOnlyList<ToolDefinition> Definitions { get; }

    public ToolRegistry(IEnumerable<ITool> tools)
    {
        var list = (tools ?? Enumerable.Empty<ITool>()).ToList();
        _tools = list.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        Definitions = list
            .Select(t => new ToolDefinition(t.Name, t.Description, t.ParametersSchemaJson))
            .ToList();
    }

    public async Task<ToolResult> ExecuteAsync(string name, string argumentsJson, CancellationToken ct = default)
    {
        if (!_tools.TryGetValue(name, out var tool))
        {
            return new ToolResult(name, "{}", false, $"Unknown tool '{name}'.");
        }

        try
        {
            return await tool.ExecuteAsync(argumentsJson, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ToolResult(name, "{}", false, ex.Message);
        }
    }
}