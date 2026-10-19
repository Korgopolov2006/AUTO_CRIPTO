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
    /// Windows явно требует перезагрузки (обновления или установка компонентов). Файлы, ожидающие замены при загрузке,
    /// не учитываются: они остаются на многих исправных компьютерах и ничего не говорят о состоянии установки.
    /// </summary>
    public static bool IsRebootPending()
    {
        try
        {
            foreach (var subKey in new[]
                     {
                         @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
                         @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"
                     })
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subKey);
                if (key is not null)
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // Нет доступа к реестру — считаем, что перезагрузка не требуется.
        }

        return false;
    }
}
