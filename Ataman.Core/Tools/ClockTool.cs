using System.Globalization;
using System.Text.Json;
using Ataman.Core.Model;

namespace Ataman.Core.Tools;

/// <summary>
/// Returns the current local date/time with a Turkish weekday name. Fully
/// offline and deterministic — ideal for "saat kaç?", "bugün günlerden ne?".
/// </summary>
public sealed class ClockTool : ITool
{
    public string Name => "get_time";

    public string Description =>
        "Get the current local date and time. Use when the user asks what time or date it is, " +
        "what day of the week, or similar time questions.";

    public string? ParametersSchemaJson => null;

    public Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        var now = DateTimeOffset.Now;
        var turkish = CultureInfo.GetCultureInfo("tr-TR");
        var result = JsonSerializer.Serialize(new
        {
            iso = now.ToString("O", CultureInfo.InvariantCulture),
            date = now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            time = now.ToString("HH:mm", CultureInfo.InvariantCulture),
            weekday = now.ToString("dddd", turkish),
        });

        return Task.FromResult(new ToolResult(Name, result, true, null));
    }
}