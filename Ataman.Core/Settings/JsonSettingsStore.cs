using System.Text.Json;

namespace Ataman.Core.Settings;

/// <summary>JSON-backed settings store in the application data directory.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private const string FileName = "settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;

    public JsonSettingsStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, FileName);
    }

    public async Task<AssistantSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
        {
            return new AssistantSettings();
        }

        var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<AssistantSettings>(json, JsonOptions) ?? new AssistantSettings();
    }

    public async Task SaveAsync(AssistantSettings settings, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct).ConfigureAwait(false);
    }

    public string FilePath => _filePath;
}