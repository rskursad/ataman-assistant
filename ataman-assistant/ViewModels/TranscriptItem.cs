using Ataman.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AtamanAssistant.ViewModels;

/// <summary>
/// A reactive chat transcript item for the UI.
/// Mutating <see cref="Text"/> during token streaming only updates the bound
/// TextBlock, avoiding expensive collection-changed and layout thrashing in Avalonia.
/// </summary>
public partial class TranscriptItem : ObservableObject
{
    public TranscriptItem(
        ChatRole role,
        string text,
        string? translatedText = null,
        bool isStreaming = false,
        DateTimeOffset? timestamp = null)
    {
        Role = role;
        Text = text;
        TranslatedText = translatedText;
        IsStreaming = isStreaming;
        Timestamp = timestamp ?? DateTimeOffset.Now;
    }

    public ChatRole Role { get; }
    public DateTimeOffset Timestamp { get; }

    [ObservableProperty]
    private string text = string.Empty;

    [ObservableProperty]
    private string? translatedText;

    [ObservableProperty]
    private bool isStreaming;

    public bool IsUser => Role == ChatRole.User;
    public bool IsAssistant => Role == ChatRole.Assistant;
    public bool HasTranslation => !string.IsNullOrWhiteSpace(TranslatedText);

    public string Header => Role switch
    {
        ChatRole.User => "Sen",
        ChatRole.Assistant => "Ataman",
        _ => "Sistem",
    };

    public string FormattedTime => Timestamp.ToString("HH:mm");

    public void AppendToken(string token)
    {
        Text += token;
    }
}
