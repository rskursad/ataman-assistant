using System.Text.Json;
using Ataman.Core.Device;
using Ataman.Core.Model;

namespace Ataman.Core.Tools;

/// <summary>
/// Device control through <see cref="IDeviceAccess"/> (mostly Android
/// accessibility). Degrades gracefully to a failure result when the host does
/// not provide device access.
/// </summary>
public sealed class DeviceTool : ITool
{
    private readonly IDeviceAccess? _deviceAccess;

    public DeviceTool(IDeviceAccess? deviceAccess = null) => _deviceAccess = deviceAccess;

    public string Name => "device";

    public string Description =>
        "Control the device: open an app by package id ('open_app'), go back ('back'), " +
        "go to home ('home'), or dump the current screen text ('screen'). Requires accessibility access.";

    public string? ParametersSchemaJson =>
        "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"enum\":[\"open_app\",\"back\",\"home\",\"screen\"]},\"package\":{\"type\":\"string\"}},\"required\":[\"action\"]}";

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        if (_deviceAccess is null)
        {
            return new ToolResult(Name, "{}", false, "Device access is not available on this platform.");
        }

        string action;
        string? package = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            if (!doc.RootElement.TryGetProperty("action", out var actionEl))
            {
                return new ToolResult(Name, "{}", false, "Missing 'action' argument.");
            }

            action = actionEl.GetString() ?? string.Empty;
            if (doc.RootElement.TryGetProperty("package", out var packageEl))
            {
                package = packageEl.GetString();
            }
        }
        catch (JsonException ex)
        {
            return new ToolResult(Name, "{}", false, $"Invalid arguments JSON: {ex.Message}");
        }

        return action switch
        {
            "open_app" when !string.IsNullOrWhiteSpace(package) =>
                await WrapResultAsync(_deviceAccess.OpenAppAsync(package, ct), "open_app", $"OpenAppAsync('{package}')"),
            "open_app" =>
                new ToolResult(Name, "{}", false, "Missing 'package' argument for open_app."),
            "back" =>
                await WrapResultAsync(_deviceAccess.PerformGlobalActionAsync(GlobalAction.Back, ct), "back", null),
            "home" =>
                await WrapResultAsync(_deviceAccess.PerformGlobalActionAsync(GlobalAction.Home, ct), "home", null),
            "screen" =>
                await ReadScreenAsync(),
            _ =>
                new ToolResult(Name, "{}", false, $"Unknown action '{action}'. Allowed: open_app, back, home, screen."),
        };
    }

    private async Task<ToolResult> WrapResultAsync(Task<bool> task, string action, string? detail)
    {
        bool ok;
        string error = "Action failed.";
        try
        {
            ok = await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ok = false;
            error = ex.Message;
        }

        return ok
            ? new ToolResult(Name, JsonSerializer.Serialize(new { action, ok = true, detail }), true, null)
            : new ToolResult(Name, JsonSerializer.Serialize(new { action, ok = false }), false, error);
    }

    private async Task<ToolResult> ReadScreenAsync()
    {
        try
        {
            var screen = await _deviceAccess!.ReadCurrentScreenAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(screen))
            {
                return new ToolResult(Name, "{}", false, "The current screen has no readable text.");
            }

            const int maxChars = 3000;
            var truncated = screen.Length > maxChars ? screen[..maxChars] + "…" : screen;
            return new ToolResult(Name, JsonSerializer.Serialize(new { screen = truncated }), true, null);
        }
        catch (Exception ex)
        {
            return new ToolResult(Name, "{}", false, ex.Message);
        }
    }
}