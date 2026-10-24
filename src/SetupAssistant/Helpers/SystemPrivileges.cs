using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace SetupAssistant.Helpers;

/// <summary>
/// Доступ к разделам реестра, закрытым списком доступа (например, контейнеры другого пользователя в ветке КриптоПро или её «Random»).
/// Вместо смены владельца используются привилегии резервного копирования и восстановления: разделы открываются в обход
/// списка доступа и сохраняют свои права.
/// </summary>
public static class SystemPrivileges
{
    private const uint RegOptionBackupRestore = 0x00000004;
    private const int KeyRead = 0x20019;
    private const int ErrorSuccess = 0;
    private const int ErrorFileNotFound = 2;
    private const uint SePrivilegeEnabled = 0x00000002;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;

    private static bool _enabled;

    /// <summary>
    /// Включает SeBackupPrivilege и SeRestorePrivilege для процесса (есть у администратора, но по умолчанию выключены). Они действуют
    /// только там, где об этом просят явно: открытие раздела с REG_OPTION_BACKUP_RESTORE и каталогов (их .NET открывает с
    /// FILE_FLAG_BACKUP_SEMANTICS — поэтому видны и закрытые папки). Без прав администратора ничего не меняется.
    /// </summary>
    public static bool EnableBackupRestore()
    {
        if (_enabled)
        {
            return true;
        }

        _enabled = EnablePrivilege("SeBackupPrivilege") & EnablePrivilege("SeRestorePrivilege");
        return _enabled;
    }

    /// <summary>
    /// Открывает подраздел в обход списка доступа (нужны привилегии из <see cref="EnableBackupRestore"/>). С обеими привилегиями
    /// раздел доступен на чтение, запись и удаление. null — раздела нет.
    /// </summary>
    public static RegistryKey? OpenForBackup(RegistryKey parent, string subKey)
    {
        EnableBackupRestore();
        var error = RegOpenKeyExW(parent.Handle, subKey, RegOptionBackupRestore, KeyRead, out var handle);
        if (error == ErrorFileNotFound)
        {
            return null;
        }

        if (error != ErrorSuccess)
        {
            throw new Win32Exception(error);
        }

        return RegistryKey.FromHandle(new SafeRegistryHandle(handle, ownsHandle: true), parent.View);
    }

    /// <summary>Открывает подраздел обычным способом, а если список доступа не пускает — в обход него.</summary>
    public static RegistryKey? OpenReadable(RegistryKey parent, string subKey)
    {
        try
        {
            return parent.OpenSubKey(subKey);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return OpenForBackup(parent, subKey);
        }
    }

    /// <summary>Удаляет раздел со всеми подразделами в обход списков доступа (каждый подраздел открывается с правом удаления).</summary>
    public static void DeleteTreeForcibly(RegistryKey parent, string subKey)
    {
        using var key = OpenForBackup(parent, subKey);
        if (key is null)
        {
            return;
        }

        foreach (var child in key.GetSubKeyNames())
        {
            DeleteTreeForcibly(key, child);
        }

        var status = NtDeleteKey(key.Handle);
        if (status != 0)
        {
            throw new Win32Exception(RtlNtStatusToDosError(status));
        }
    }

    private static bool EnablePrivilege(string name)
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        if (!OpenProcessToken(process.Handle, TokenAdjustPrivileges | TokenQuery, out var token))
        {
            return false;
        }

        try
        {
            if (!LookupPrivilegeValueW(null, name, out var luid))
            {
                return false;
            }

            var privileges = new TokenPrivileges { PrivilegeCount = 1, Luid = luid, Attributes = SePrivilegeEnabled };
            // AdjustTokenPrivileges возвращает успех и тогда, когда привилегии у токена нет: это видно только по последней ошибке.
            return AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero)
                   && Marshal.GetLastWin32Error() == ErrorSuccess;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupPrivilegeValueW(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAll, ref TokenPrivileges newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegOpenKeyExW(SafeRegistryHandle key, string subKey, uint options, int samDesired, out IntPtr result);

    [DllImport("ntdll.dll")]
    private static extern int NtDeleteKey(SafeRegistryHandle key);

    [DllImport("ntdll.dll")]
    private static extern int RtlNtStatusToDosError(int status);
}
