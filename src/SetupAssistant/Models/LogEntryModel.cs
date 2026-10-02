using Microsoft.Extensions.Logging;

namespace SetupAssistant.Models;

/// <summary>Одна запись журнала действий приложения для отображения в UI и записи в файл.</summary>
public sealed class LogEntryModel
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public LogLevel Level { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string? ErrorText { get; set; }

    public override string ToString() =>
        $"{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {Action} — {Result}" +
        (string.IsNullOrEmpty(ErrorText) ? string.Empty : $" | Ошибка: {ErrorText}");
}
