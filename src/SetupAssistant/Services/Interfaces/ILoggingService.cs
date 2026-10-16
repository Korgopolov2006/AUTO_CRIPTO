using Microsoft.Extensions.Logging;
using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Запись журнала действий приложения в файл и предоставление записей для UI.</summary>
public interface ILoggingService
{
    IReadOnlyList<LogEntryModel> Entries { get; }

    event EventHandler<LogEntryModel>? EntryAdded;

    void Log(LogLevel level, string action, string result, string? errorText = null);

    void Info(string action, string result);

    void Warning(string action, string result, string? errorText = null);

    void Error(string action, string result, string? errorText = null);

    string CurrentLogFilePath { get; }
}
