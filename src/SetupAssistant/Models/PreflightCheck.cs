namespace SetupAssistant.Models;

/// <summary>Результат одной предварительной проверки перед установкой.</summary>
/// <param name="Status">Ok — всё хорошо, Warning — можно продолжать, Error — продолжать нельзя.</param>
/// <param name="Advice">Что сделать, чтобы устранить причину; null, если проверка пройдена.</param>
/// <param name="ClosablePids">Программы, которые мешают и которые можно попросить закрыться (кнопка «Закрыть мешающие программы»).</param>
public sealed record PreflightCheck(string Name, StatusLevel Status, string Message, string? Advice = null, IReadOnlyList<int>? ClosablePids = null)
{
    public bool IsBlocking => Status == StatusLevel.Error;

    public bool HasAdvice => !string.IsNullOrWhiteSpace(Advice);

    /// <summary>Текст для диалога и консоли: «название: сообщение» и, если есть, «Что сделать: …».</summary>
    public string ToText() => HasAdvice ? $"{Name}: {Message}\n   Что сделать: {Advice}" : $"{Name}: {Message}";
}
