namespace SetupAssistant.Helpers;

/// <summary>Состояние службы Windows Installer: одновременно может выполняться только одна установка.</summary>
public static class WindowsInstallerState
{
    /// <summary>Истина, если сейчас идёт другая установка MSI (занят мьютекс <c>Global\_MSIExecute</c>).</summary>
    public static bool IsBusy()
    {
        try
        {
            if (Mutex.TryOpenExisting(@"Global\_MSIExecute", out var mutex))
            {
                mutex.Dispose();
                return true;
            }
        }
        catch (Exception)
        {
            // Нет доступа к мьютексу — считаем, что занятости нет.
        }

        return false;
    }

    /// <summary>
    /// Части путей компонентов, которые ставит и удаляет приложение. Из отложенных операций с файлами важны только они:
    /// чужие записи (например, «Игровые службы» Microsoft восстанавливают свою при каждой загрузке) ничего не говорят о настройке.
    /// </summary>
    private static readonly string[] ManagedPathMarkers =
    {
        "Crypto Pro", "CryptoPro", "Rutoken", "Aktiv", "Chromium", "SkbKontur", "Rostelecom", "IFCPlugin"
    };

    /// <summary>Windows требует перезагрузки по причине, важной для настройки (см. <see cref="GetPendingRebootReason"/>).</summary>
    public static bool IsRebootPending() => GetPendingRebootReason() is not null;

    /// <summary>
    /// Причина, по которой перед настройкой стоит перезагрузить компьютер: обновления Windows или отложенное удаление и замена
    /// файлов наших компонентов. null — ожидающей перезагрузки нет (посторонние отложенные операции не считаются).
    /// </summary>
    public static string? GetPendingRebootReason()
    {
        try
        {
            if (KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending")
                || KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
            {
                return "Windows ждёт перезагрузки после обновления";
            }

            using var sessionManager = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            if (sessionManager?.GetValue("PendingFileRenameOperations") is string[] operations)
            {
                var ours = operations
                    .Where(path => ManagedPathMarkers.Any(marker => path.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                    .Select(path => System.IO.Path.GetFileName(path.TrimEnd('\\')))
                    .Where(name => name.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList();

                if (ours.Count > 0)
                {
                    return $"после удаления или обновления компонентов ждёт перезагрузки замена файлов ({string.Join(", ", ours)})";
                }
            }
        }
        catch (Exception)
        {
            // Нет доступа к реестру — считаем, что перезагрузка не требуется.
        }

        return null;
    }

    private static bool KeyExists(string subKey)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subKey);
        return key is not null;
    }
}
