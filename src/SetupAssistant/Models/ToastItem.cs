namespace SetupAssistant.Models;

public enum ToastKind
{
    Success,
    Info,
    Warning,
    Error
}

/// <summary>Короткое неблокирующее уведомление в углу окна.</summary>
public sealed class ToastItem
{
    public required string Title { get; init; }
    public string? Message { get; init; }
    public ToastKind Kind { get; init; }

    public string Glyph => Kind switch
    {
        ToastKind.Success => "",
        ToastKind.Warning => "",
        ToastKind.Error => "",
        _ => ""
    };

    public bool HasMessage => !string.IsNullOrEmpty(Message);
}
