using Microsoft.Win32;
using SetupAssistant.Models;

namespace SetupAssistant.Helpers;

/// <summary>Однократный автозапуск приложения после перезагрузки: оно продолжит настройку (аргумент --resume) с тех же шагов.</summary>
public static class ResumeRegistration
{
    private const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string ValueName = "SetupAssistantResume";
    private const string StateKey = @"Software\SetupAssistant";
    private const string StepsValueName = "ResumeSteps";

    /// <summary>Регистрирует автозапуск и запоминает шаги, которые нужно выполнить после перезагрузки.</summary>
    public static void Register(IEnumerable<SetupStepKind> steps)
    {
        var exe = Environment.ProcessPath
                  ?? throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу приложения");

        using (var state = Registry.CurrentUser.CreateSubKey(StateKey, writable: true))
        {
            state.SetValue(StepsValueName, string.Join(",", steps.Select(s => s.ToString())));
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunOnceKey, writable: true);
        key.SetValue(ValueName, $"\"{exe}\" --resume");
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

    public static void Unregister()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunOnceKey, writable: true))
        {
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        using var state = Registry.CurrentUser.OpenSubKey(StateKey, writable: true);
        state?.DeleteValue(StepsValueName, throwOnMissingValue: false);
    }
}
