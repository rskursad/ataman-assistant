using Ataman.Core.Model;

namespace AtamanAssistant.ViewModels;

/// <summary>
/// A single chat line rendered as a bubble. <see cref="IsUser"/> balloons to
/// the right with an accent fill; assistant/note messages balloon left.
/// </summary>
public class ChatMessageViewModel : ViewModelBase
{
    private string _text;

    public ChatMessageViewModel(ChatRole role, string text)
    {
        Role = role;
        _text = text;
    }

    public ChatRole Role { get; }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public bool IsUser => Role == ChatRole.User;
    public bool IsAssistant => Role == ChatRole.Assistant;
    public bool IsNote => Role is ChatRole.System or ChatRole.Tool;

    /// <summary>Prefix used in the payload (keeps the old "Sen/Ataman:" transcript style).</summary>
    public string DisplayName => Role switch
    {
        ChatRole.User => "Sen",
        ChatRole.Assistant => "Ataman",
        _ => "Sistem",
    };

    public static ChatMessageViewModel User(string text) => new(ChatRole.User, text);
    public static ChatMessageViewModel Assistant(string text) => new(ChatRole.Assistant, text);
    public static ChatMessageViewModel Note(string text) => new(ChatRole.System, text);
}