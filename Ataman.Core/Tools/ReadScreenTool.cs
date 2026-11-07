using System.Text.Json;
using Ataman.Core.Device;
using Ataman.Core.Model;

namespace Ataman.Core.Tools;

/// <summary>
/// Reads the visible text of the current screen (Android accessibility).
/// Degrades gracefully when the host provides no device access.
/// </summary>
public sealed class ReadScreenTool : ITool
{
    private readonly IDeviceAccess? _deviceAccess;

    public ReadScreenTool(IDeviceAccess? deviceAccess = null) => _deviceAccess = deviceAccess;

    public string Name => "read_screen";

    public string Description =>
        "Read the text content of the currently visible screen. Use when the user asks " +
        "what is on the screen or what an app shows. Requires accessibility access.";

    public string? ParametersSchemaJson => null;

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        if (_deviceAccess is null)
        {
            return new ToolResult(Name, "{}", false, "Device access is not available on this platform.");
        }

        string? screen;
        try
        {
            screen = await _deviceAccess.ReadCurrentScreenAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ToolResult(Name, "{}", false, ex.Message);
        }

        if (string.IsNullOrWhiteSpace(screen))
        {
            return new ToolResult(Name, "{}", false, "The current screen has no readable text.");
        }

        const int maxChars = 3000;
        var truncated = screen.Length > maxChars ? screen[..maxChars] + "…" : screen;
        return new ToolResult(Name, JsonSerializer.Serialize(new { screen = truncated }), true, null);
    }
}