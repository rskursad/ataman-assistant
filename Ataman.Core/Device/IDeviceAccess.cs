namespace Ataman.Core.Device;

/// <summary>Global navigational actions (mostly powered by AccessibilityService).</summary>
public enum GlobalAction
{
    Back,
    Home,
    Recents,
    Notifications,
    QuickSettings,
    Screenshot,
    PowerDialog,
}

/// <summary>
/// Host-level device access. The full range is only available on Android
/// through the Accessibility Service; other platforms degrade gracefully.
/// </summary>
public interface IDeviceAccess
{
    bool IsAccessibilityGranted { get; }
    bool IsListening { get; }

    /// <summary>Prompts the user to grant Accessibility access (Android).</summary>
    Task<bool> RequestAccessibilityAsync(CancellationToken ct = default);

    Task<bool> OpenAppAsync(string packageId, CancellationToken ct = default);
    Task<bool> PerformGlobalActionAsync(GlobalAction action, CancellationToken ct = default);

    /// <summary>Dumps the accessibility tree of the current screen as text.</summary>
    Task<string?> ReadCurrentScreenAsync(CancellationToken ct = default);

    Task<bool> TapAsync(string nodeId, CancellationToken ct = default);
}