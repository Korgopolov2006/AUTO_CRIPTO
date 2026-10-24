using Microsoft.Win32;
using SetupAssistant.Models;

namespace SetupAssistant.Helpers;

/// <summary>
/// Однократный автозапуск приложения после перезагрузки (аргумент --resume): оно продолжит настройку с тех же шагов
/// или завершит полную очистку.
/// </summary>
public static class ResumeRegistration
{
    private const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string ValueName = "SetupAssistantResume";
    private const string StateKey = @"Software\SetupAssistant";
    private const string StepsValueName = "ResumeSteps";
    private const string CleanupDirectoryValueName = "ResumeCleanupDirectory";
    private const string CleanupFlagsValueName = "ResumeCleanupFlags";
    private const string KontinentFlag = "kontinent";
    private const string ContainersFlag = "containers";

    /// <summary>Регистрирует автозапуск и запоминает шаги, которые нужно выполнить после перезагрузки.</summary>
    public static void Register(IEnumerable<SetupStepKind> steps)
    {
        using (var state = Registry.CurrentUser.CreateSubKey(StateKey, writable: true))
        {
            state.SetValue(StepsValueName, string.Join(",", steps.Select(s => s.ToString())));
        }

        RegisterRunOnce();
    }

    /// <summary>Регистрирует автозапуск для завершения полной очистки: папка отчёта и решения пользователя сохраняются.</summary>
    public static void RegisterCleanup(FullCleanupSession session)
    {
        var flags = new List<string>();
        if (session.IncludeKontinent)
        {
            flags.Add(KontinentFlag);
        }

        if (session.DeleteContainers)
        {
            flags.Add(ContainersFlag);
        }

        using (var state = Registry.CurrentUser.CreateSubKey(StateKey, writable: true))
        {
            state.SetValue(CleanupDirectoryValueName, session.BackupDirectory);
            state.SetValue(CleanupFlagsValueName, string.Join(",", flags));
        }

        RegisterRunOnce();
    }

    /// <summary>Шаги, запомненные перед перезагрузкой; пусто, если запоминать было нечего.</summary>
    public static IReadOnlyList<SetupStepKind> ReadSteps()
    {
        using var state = Registry.CurrentUser.OpenSubKey(StateKey);
        var raw = state?.GetValue(StepsValueName) as string;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<SetupStepKind>();
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(name => Enum.TryParse<SetupStepKind>(name, out var kind) ? (SetupStepKind?)kind : null)
            .Where(kind => kind is not null)
            .Select(kind => kind!.Value)
            .ToList();
    }

    /// <summary>Незавершённая полная очистка, запомненная перед перезагрузкой; null — её нет.</summary>
    public static FullCleanupSession? ReadCleanup()
    {
        using var state = Registry.CurrentUser.OpenSubKey(StateKey);
        if (state?.GetValue(CleanupDirectoryValueName) is not string directory || string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var flags = (state.GetValue(CleanupFlagsValueName) as string ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
        return new FullCleanupSession
        {
            BackupDirectory = directory,
            IncludeKontinent = flags.Contains(KontinentFlag),
            DeleteContainers = flags.Contains(ContainersFlag),
            IsResume = true
        };
    }

    public static void Unregister()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunOnceKey, writable: true))
        {
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        using var state = Registry.CurrentUser.OpenSubKey(StateKey, writable: true);
        state?.DeleteValue(StepsValueName, throwOnMissingValue: false);
        state?.DeleteValue(CleanupDirectoryValueName, throwOnMissingValue: false);
        state?.DeleteValue(CleanupFlagsValueName, throwOnMissingValue: false);
    }

    private static void RegisterRunOnce()
    {
        var exe = Environment.ProcessPath
                  ?? throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу приложения");

        using var key = Registry.CurrentUser.CreateSubKey(RunOnceKey, writable: true);
        key.SetValue(ValueName, $"\"{exe}\" --resume");
    }
}
