using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Extensions.Logging;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="ILoggingService"/>
public sealed class LoggingService : ILoggingService
{
    private readonly object _syncRoot = new();
    private readonly ObservableCollection<LogEntryModel> _entries = new();

    public IReadOnlyList<LogEntryModel> Entries => _entries;

    public event EventHandler<LogEntryModel>? EntryAdded;

    public string CurrentLogFilePath { get; }

    public LoggingService(IConfigService configService)
    {
        var config = configService.Current.Logging;
        var logDirectory = configService.ResolvePath(config.LogDirectory);
        Directory.CreateDirectory(logDirectory);

        CurrentLogFilePath = Path.Combine(logDirectory, string.Format(config.LogFileNamePattern, DateTime.Now));

        CleanupOldLogs(logDirectory, config.RetainDays);
    }

    public void Log(LogLevel level, string action, string result, string? errorText = null)
    {
        var entry = new LogEntryModel
        {
            Timestamp = DateTime.Now,
            Level = level,
            Action = action,
            Result = result,
            ErrorText = errorText
        };

        lock (_syncRoot)
        {
            WriteToFile(entry);
        }

        App.Current?.Dispatcher.Invoke(() => _entries.Add(entry));
        EntryAdded?.Invoke(this, entry);
    }

    public void Info(string action, string result) => Log(LogLevel.Information, action, result);

    public void Warning(string action, string result, string? errorText = null) => Log(LogLevel.Warning, action, result, errorText);

    public void Error(string action, string result, string? errorText = null) => Log(LogLevel.Error, action, result, errorText);

    private void WriteToFile(LogEntryModel entry)
    {
        try
        {
            File.AppendAllText(CurrentLogFilePath, entry + Environment.NewLine);
        }
        catch (IOException)
        {
            // Игнорируем временную блокировку файла журнала — запись останется в UI-коллекции.
        }
    }

    private static void CleanupOldLogs(string logDirectory, int retainDays)
    {
        try
        {
            var threshold = DateTime.Now.AddDays(-retainDays);
            foreach (var file in Directory.EnumerateFiles(logDirectory, "*.log"))
            {
                if (File.GetLastWriteTime(file) < threshold)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
            // Очистка старых журналов не критична для работы приложения.
        }
    }
}
