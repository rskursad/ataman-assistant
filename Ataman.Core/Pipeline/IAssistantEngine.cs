using Ataman.Core.Model;

namespace Ataman.Core.Pipeline;

public enum AssistantState
{
    Idle,
    WaitingForWakeWord,
    WakingUp,
    ListeningForCommand,
    Thinking,
    Speaking,
    Error,
}

public sealed record TranscriptEntry(DateTimeOffset Timestamp, ChatRole Role, string Text, string? TranslatedText = null);

/// <summary>
/// Facade the UI talks to. Orchestrates: wake word -> STT -> (optional
/// translation) -> LLM (optional tool calls) -> (optional translation) -> TTS.
/// Manages the voice lifecycle state machine to prevent acoustic feedback.
/// </summary>
public interface IAssistantEngine : IAsyncDisposable
{
    event EventHandler<string>? WakeWordDetected;
    event EventHandler<TranscriptEntry>? TranscriptAdded;
    event EventHandler<string>? TokenProduced;
    event EventHandler<string>? PartialSpeechRecognized;
    event EventHandler<string>? StatusChanged;
    event EventHandler<AssistantState>? StateChanged;

    AssistantState State { get; }
    IReadOnlyList<TranscriptEntry> Transcript { get; }

    Task InitializeAsync(CancellationToken ct = default);
    Task StartListeningAsync(CancellationToken ct = default);
    Task StopListeningAsync();

    /// <summary>Runs a single spoken/text command through the whole pipeline.</summary>
    Task<string> ProcessCommandAsync(string text, CancellationToken ct = default);
}