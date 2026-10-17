using Microsoft.Win32;

namespace SetupAssistant.Helpers;

/// <summary>Однократный автозапуск приложения после перезагрузки: оно продолжит настройку (аргумент --resume).</summary>
public static class ResumeRegistration
{
    private const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string ValueName = "SetupAssistantResume";

    public static void Register()
    {
        var exe = Environment.ProcessPath
                  ?? throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу приложения");

        using var key = Registry.CurrentUser.CreateSubKey(RunOnceKey, writable: true);
        key.SetValue(ValueName, $"\"{exe}\" --resume");
    }

    public static void Unregister()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunOnceKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
