using Ataman.Core.Model;

namespace Ataman.Core.Pipeline;

public sealed record TranscriptEntry(DateTimeOffset Timestamp, ChatRole Role, string Text);

/// <summary>
/// Facade the UI talks to. Orchestrates: wake word → STT → (optional
/// translation) → LLM (optional tool calls) → (optional translation) → TTS.
/// Platform implementations are injected by the host.
/// </summary>
public interface IAssistantEngine : IAsyncDisposable
{
    event EventHandler<string>? WakeWordDetected;
    event EventHandler<TranscriptEntry>? TranscriptAdded;
    event EventHandler<string>? StatusChanged;

    IReadOnlyList<TranscriptEntry> Transcript { get; }

    Task InitializeAsync(CancellationToken ct = default);
    Task StartListeningAsync(CancellationToken ct = default);
    Task StopListeningAsync();

    /// <summary>Runs a single spoken/text command through the whole pipeline.</summary>
    Task<string> ProcessCommandAsync(string text, CancellationToken ct = default);
}