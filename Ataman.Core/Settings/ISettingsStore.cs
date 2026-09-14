namespace Ataman.Core.Settings;

public interface ISettingsStore
{
    Task<AssistantSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AssistantSettings settings, CancellationToken ct = default);
}