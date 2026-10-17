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
}
