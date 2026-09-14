namespace Ataman.Core.Model;

/// <summary>A file that has been fully downloaded into the model store.</summary>
public sealed record DownloadedFile(string FilePath, long Size);

/// <summary>
/// Downloads GGUF / STT / TTS model artifacts once into the app data folder,
/// with progress reporting and cancellation. APK stays small; weights are
/// fetched on first run.
/// </summary>
public sealed class ModelManager
{
    private const int BufferSize = 81_920;
    private const string PartSuffix = ".part";

    public ModelManager(string directoryPath)
    {
        DirectoryPath = directoryPath;
        Directory.CreateDirectory(directoryPath);
    }

    public string DirectoryPath { get; }

    public string GetFilePath(string fileName) => Path.Combine(DirectoryPath, fileName);

    public string GetFilePathByUrl(string url, string extension)
    {
        var name = UrlToFileName(url, extension);
        return Path.Combine(DirectoryPath, name);
    }

    public bool IsDownloaded(string filePath) =>
        File.Exists(filePath) && new FileInfo(filePath).Length > 0;

    public async Task<DownloadedFile> DownloadAsync(
        string url,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(DirectoryPath);

        if (IsDownloaded(destinationPath))
        {
            return new DownloadedFile(destinationPath, new FileInfo(destinationPath).Length);
        }

        var tempPath = destinationPath + PartSuffix;
        using var client = new HttpClient();

        // No default User-Agent is sent by HttpClient; some hosts require one.
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("AtamanAssistant/1.0 (+https://example.invalid)");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var destination = new FileStream(
            tempPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        var buffer = new byte[BufferSize];
        long received = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;

            if (totalBytes is > 0)
            {
                progress?.Report((double)received / totalBytes.Value);
            }
        }

        await destination.FlushAsync(ct).ConfigureAwait(false);
        destination.Close();

        File.Move(tempPath, destinationPath, overwrite: true);
        return new DownloadedFile(destinationPath, received);
    }

    /// <summary>
    /// Downloads a .zip artifact (e.g. a Vosk model) and extracts it into
    /// <paramref name="extractToDirectory"/>. The folder name inside the zip
    /// becomes the model directory. Already-extracted folders are skipped.
    /// </summary>
    public async Task<string> DownloadAndExtractZipAsync(
        string zipUrl,
        string extractToDirectory,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var zipPath = GetFilePathByUrl(zipUrl, ".zip");
        await DownloadAsync(zipUrl, zipPath, progress, ct).ConfigureAwait(false);

        System.IO.Directory.CreateDirectory(extractToDirectory);

        // The zip is stored inside extractToDirectory, so the directory existing
        // does NOT prove it was already extracted. Skip extraction only when a real
        // model folder (recognised by its am/conf markers) is already present.
        if (!System.IO.Directory.EnumerateDirectories(extractToDirectory).Any(IsModelDirectory))
        {
            await Task.Run(
                () => System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractToDirectory, overwriteFiles: true),
                ct).ConfigureAwait(false);
        }

        var inner = System.IO.Directory
            .EnumerateDirectories(extractToDirectory)
            .Select(d => new DirectoryInfo(d))
            .OrderByDescending(d => d.Name.Length)
            .FirstOrDefault();

        if (inner is null)
        {
            throw new InvalidOperationException($"Zip '{zipPath}' içinde model klasörü bulunamadı.");
        }

        return inner.FullName;
    }

    /// <summary>
/// Recognises an extracted model folder. Vosk ships both layouts: classic
/// (am/final.mdl, conf/model.conf) and the newer flat one (final.mdl at root).
/// </summary>
    private static bool IsModelDirectory(string directory) =>
        File.Exists(Path.Combine(directory, "final.mdl"))
        || File.Exists(Path.Combine(directory, "am", "final.mdl"))
        || File.Exists(Path.Combine(directory, "conf", "model.conf"));

    private static string UrlToFileName(string url, string extension)
    {
        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"model{DateTime.UtcNow:yyyyMMddHHmmss}.{extension}";
        }

        return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? fileName : fileName + "." + extension;
    }
}