using System.Management;
using Microsoft.Win32;

namespace SetupAssistant.Helpers;

/// <summary>Точка восстановления Windows перед полной очисткой (WMI SystemRestore.CreateRestorePoint).</summary>
public static class RestorePoint
{
    private const int ApplicationUninstall = 1;
    private const int BeginSystemChange = 100;

    /// <summary>
    /// Создаёт точку восстановления. Windows создаёт не больше одной точки в сутки и молча пропускает лишние, поэтому успех
    /// проверяется по списку точек. Возвращает true и описание созданной точки либо false и причину.
    /// </summary>
    public static (bool Created, string Message) Create(string description)
    {
        // Выключенную защиту видно заранее: WMI в этом случае отвечает невнятной ошибкой без текста.
        if (DisabledReason() is { } reason)
        {
            return (false, reason);
        }

        try
        {
            var before = Latest();
            using var restore = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null);
            using var parameters = restore.GetMethodParameters("CreateRestorePoint");
            parameters["Description"] = description;
            parameters["RestorePointType"] = ApplicationUninstall;
            parameters["EventType"] = BeginSystemChange;
            using var result = restore.InvokeMethod("CreateRestorePoint", parameters, null);
            var code = Convert.ToUInt32(result?["ReturnValue"] ?? 0u);
            if (code != 0)
            {
                return (false, code == 1058
                    ? "Защита системы выключена — точка восстановления не создана"
                    : $"Точка восстановления не создана (код {code})");
            }

            var after = Latest();
            if (after is not null && after.Value.Sequence != before?.Sequence)
            {
                return (true, $"Создана точка восстановления «{description}» ({after.Value.Created:dd.MM.yyyy HH:mm})");
            }

            return (false, before is null
                ? "Windows не создала точку восстановления (возможно, защита системы выключена)"
                : $"Windows не создаёт точки чаще раза в сутки; последняя — {before.Value.Created:dd.MM.yyyy HH:mm} «{before.Value.Description}»");
        }
        catch (Exception ex)
        {
            var details = string.IsNullOrWhiteSpace(ex.Message) ? $"{ex.GetType().Name}, код 0x{ex.HResult:X8}" : ex.Message.Trim();
            return (false, $"Точка восстановления не создана: {details}");
        }
    }

    /// <summary>Почему защита системы не работает (отключена политикой или выключена для всех дисков); null — включена.</summary>
    private static string? DisabledReason()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using (var policy = hklm.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore"))
            {
                if (policy?.GetValue("DisableSR") is int disabled && disabled != 0)
                {
                    return "Защита системы отключена групповой политикой — точка восстановления не создана";
                }
            }

            // RPSessionInterval = 0: защита выключена на всех дисках.
            using var settings = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
            if (settings?.GetValue("RPSessionInterval") is int interval && interval == 0)
            {
                return "Защита системы выключена — точка восстановления не создана";
            }
        }
        catch (Exception)
        {
            // Не удалось прочитать настройки — пробуем создать точку как обычно.
        }

        return null;
    }

    private static (uint Sequence, DateTime Created, string Description)? Latest()
    {
        using var searcher = new ManagementObjectSearcher(@"root\default", "SELECT SequenceNumber, CreationTime, Description FROM SystemRestore");
        (uint, DateTime, string)? latest = null;
        foreach (var point in searcher.Get())
        {
            using (point)
            {
                var sequence = Convert.ToUInt32(point["SequenceNumber"]);
                if (latest is null || sequence > latest.Value.Item1)
                {
                    latest = (sequence, ManagementDateTimeConverter.ToDateTime(point["CreationTime"] as string ?? string.Empty), point["Description"] as string ?? string.Empty);
                }
            }
        }

        return latest;
    }
}
