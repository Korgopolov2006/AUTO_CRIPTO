using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SetupAssistant.Models;

namespace SetupAssistant.Helpers;

/// <param name="Removed">Удалено сразу.</param>
/// <param name="Scheduled">Удалится при перезагрузке (занятые файлы, работающий драйвер).</param>
/// <param name="Failed">Не удалось, с причиной.</param>
public sealed record CleanupOutcome(
    IReadOnlyList<CleanupItem> Removed,
    IReadOnlyList<CleanupItem> Scheduled,
    IReadOnlyList<(CleanupItem Item, string Error)> Failed);

/// <summary>
/// Удаление найденных <see cref="CryptoTraceScanner"/> следов. Каждый раздел и значение реестра перед изменением сохраняется в .reg;
/// запись, которую сохранить не удалось, не трогается. Общие списки Windows правятся точечно, функции CNG снимаются через
/// BCryptRemoveContextFunction/BCryptRemoveContextFunctionProvider, занятые файлы удаляются при перезагрузке.
/// </summary>
public static class CryptoTraceRemover
{
    private const uint CryptLocal = 1;
    private const int StatusNotFound = unchecked((int)0xC0000225);
    private const uint MoveFileDelayUntilReboot = 0x4;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceMarkedForDelete = 1072;

    private static readonly RegistryKey Hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
    private static readonly RegistryKey Hku = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);

    /// <param name="registryBackupFile">Файл .reg, куда дописываются удаляемые разделы и значения.</param>
    /// <param name="allowContainers">Пользователь подтвердил удаление ключевых контейнеров (резервная копия уже сделана).</param>
    /// <param name="log">Запись в журнал приложения (только отложенное и неудачи): действие и подробности.</param>
    public static CleanupOutcome Remove(IReadOnlyList<CleanupItem> items, string registryBackupFile, bool allowContainers, Action<string, string?> log)
    {
        SystemPrivileges.EnableBackupRestore();

        var removed = new List<CleanupItem>();
        var scheduled = new List<CleanupItem>();
        var failed = new List<(CleanupItem, string)>();

        using var backup = new RegFileWriter(registryBackupFile);
        backup.WriteComment($"Полная очистка СКЗИ, {DateTime.Now:dd.MM.yyyy HH:mm:ss}: разделы и значения до удаления");

        void Run(CleanupItem item, Func<CleanupItem, RegFileWriter, bool?> action)
        {
            if (item.IsContainerData && !allowContainers)
            {
                failed.Add((item, "ключевые контейнеры удаляются только после отдельного подтверждения"));
                return;
            }

            try
            {
                // true — удалено, null — удалится при перезагрузке.
                // Полный список удалённого — в report.txt; в журнал приложения попадает только то, что требует внимания.
                var result = action(item, backup);
                (result == true ? removed : scheduled).Add(item);
                if (result != true)
                {
                    log("Удалится при перезагрузке", item.ToString());
                }
            }
            catch (Exception ex)
            {
                failed.Add((item, ex.Message));
                log("Не удалось удалить", $"{item}: {ex.Message}");
            }
        }

        // Порядок важен: сначала службы (до их файлов), функции CNG — до регистрации поставщика, вложенные разделы — один раз.
        foreach (var item in items.Where(i => i.Kind == CleanupItemKind.Service))
        {
            Run(item, RemoveService);
        }

        var cngItems = items.Where(i => i.Kind is CleanupItemKind.CngFunctionProvider or CleanupItemKind.CngFunction or CleanupItemKind.CngProvider).ToList();
        if (cngItems.Count > 0)
        {
            BackupKey(backup, @"HKLM\SYSTEM\CurrentControlSet\Control\Cryptography\Configuration\Local");
            foreach (var provider in cngItems.Where(i => i.Kind == CleanupItemKind.CngProvider))
            {
                BackupKey(backup, $@"HKLM\SYSTEM\CurrentControlSet\Control\Cryptography\Providers\{provider.Path}");
            }

            foreach (var item in cngItems.OrderBy(i => i.Kind switch { CleanupItemKind.CngFunctionProvider => 0, CleanupItemKind.CngFunction => 1, _ => 2 }))
            {
                Run(item, (i, _) => RemoveCng(i));
            }
        }

        foreach (var item in items.Where(i => i.Kind == CleanupItemKind.ListItem))
        {
            Run(item, RemoveListItem);
        }

        foreach (var item in items.Where(i => i.Kind == CleanupItemKind.RegistryValue))
        {
            Run(item, RemoveValue);
        }

        var keys = items.Where(i => i.Kind == CleanupItemKind.RegistryKey).OrderBy(i => i.Path.Length).ToList();
        var deletedKeys = new List<string>();
        foreach (var item in keys)
        {
            // Раздел внутри уже удалённого: удалён вместе с ним.
            if (deletedKeys.Any(parent => item.Path.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase)))
            {
                removed.Add(item);
                continue;
            }

            Run(item, RemoveKey);
            if (removed.Contains(item))
            {
                deletedKeys.Add(item.Path);
            }
        }

        foreach (var item in items.Where(i => i.Kind == CleanupItemKind.File))
        {
            Run(item, (i, _) => RemoveFile(i.Path));
        }

        foreach (var item in items.Where(i => i.Kind == CleanupItemKind.Directory))
        {
            Run(item, (i, _) => RemoveDirectory(i.Path, allowContainers));
        }

        return new CleanupOutcome(removed, scheduled, failed);
    }

    // ---------- Реестр ----------

    private static (RegistryKey Hive, string SubKey) Parse(string path)
    {
        var separator = path.IndexOf('\\');
        var prefix = path[..separator];
        var subKey = path[(separator + 1)..];
        return prefix.ToUpperInvariant() switch
        {
            "HKLM" => (Hklm, subKey),
            "HKU" => (Hku, subKey),
            _ => throw new InvalidOperationException($"Неподдерживаемый раздел реестра: {path}")
        };
    }

    /// <summary>Сохраняет раздел в .reg; false — раздела нет.</summary>
    private static bool BackupKey(RegFileWriter backup, string path, bool valuesOnly = false)
    {
        var (hive, subKey) = Parse(path);
        using var key = SystemPrivileges.OpenReadable(hive, subKey);
        if (key is null)
        {
            return false;
        }

        if (valuesOnly)
        {
            backup.WriteValues(RegFileWriter.ExpandHiveName(path), key);
        }
        else
        {
            backup.WriteTree(RegFileWriter.ExpandHiveName(path), key);
        }

        return true;
    }

    private static bool? RemoveKey(CleanupItem item, RegFileWriter backup)
    {
        if (!BackupKey(backup, item.Path))
        {
            return true;
        }

        var (hive, subKey) = Parse(item.Path);
        try
        {
            hive.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Часть подразделов закрыта списком доступа (например, «Random» или контейнеры другого пользователя у КриптоПро).
            SystemPrivileges.DeleteTreeForcibly(hive, subKey);
        }

        return true;
    }

    private static bool? RemoveValue(CleanupItem item, RegFileWriter backup)
    {
        var (hive, subKey) = Parse(item.Path);
        using var key = hive.OpenSubKey(subKey, writable: true);
        if (key is null || key.GetValue(item.ValueName) is null)
        {
            return true;
        }

        BackupKey(backup, item.Path, valuesOnly: true);
        if (item.Item is not null)
        {
            key.SetValue(item.ValueName, item.Item, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(item.ValueName!, throwOnMissingValue: false);
        }

        return true;
    }

    /// <summary>Убирает элемент из REG_MULTI_SZ или из строки через запятую; остальные элементы и их порядок сохраняются.</summary>
    private static bool? RemoveListItem(CleanupItem item, RegFileWriter backup)
    {
        var (hive, subKey) = Parse(item.Path);
        using var key = hive.OpenSubKey(subKey, writable: true)
                        ?? throw new InvalidOperationException("раздел не найден");
        var name = item.ValueName!;
        if (key.GetValue(name) is null)
        {
            return true;
        }

        BackupKey(backup, item.Path, valuesOnly: true);
        if (key.GetValueKind(name) == RegistryValueKind.MultiString)
        {
            var list = (string[])key.GetValue(name)!;
            key.SetValue(name, list.Where(e => !e.Equals(item.Item, StringComparison.OrdinalIgnoreCase)).ToArray(), RegistryValueKind.MultiString);
        }
        else
        {
            var list = (key.GetValue(name) as string ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            key.SetValue(name, string.Join(", ", list.Where(e => !e.Equals(item.Item, StringComparison.OrdinalIgnoreCase))), RegistryValueKind.String);
        }

        return true;
    }

    // ---------- CNG ----------

    private static bool? RemoveCng(CleanupItem item)
    {
        var status = item.Kind switch
        {
            CleanupItemKind.CngFunctionProvider => BCryptRemoveContextFunctionProvider(CryptLocal, item.Path, item.CngInterface, item.Item!, item.CngProvider!),
            CleanupItemKind.CngFunction => BCryptRemoveContextFunction(CryptLocal, item.Path, item.CngInterface, item.Item!),
            _ => BCryptUnregisterProvider(item.Path)
        };

        if (status != 0 && status != StatusNotFound)
        {
            throw new InvalidOperationException($"CNG вернул 0x{status:X8}");
        }

        return true;
    }

    // ---------- Службы ----------

    /// <summary>
    /// Драйвер не останавливается (на части компьютеров остановка CProCtrl зависает в ядре), а только помечается на удаление —
    /// Windows удалит его при перезагрузке. Обычная служба сначала останавливается.
    /// </summary>
    private static bool? RemoveService(CleanupItem item, RegFileWriter backup)
    {
        var path = $@"HKLM\SYSTEM\CurrentControlSet\Services\{item.Path}";
        using (var key = SystemPrivileges.OpenReadable(Hklm, $@"SYSTEM\CurrentControlSet\Services\{item.Path}"))
        {
            if (key is null)
            {
                return true;
            }

            backup.WriteTree(RegFileWriter.ExpandHiveName(path), key);
            var type = key.GetValue("Type") is int t ? t : 0;
            if (type >= 0x10)
            {
                RunSc($"stop \"{item.Path}\"");
            }
        }

        var code = RunSc($"delete \"{item.Path}\"");
        if (code == ErrorServiceDoesNotExist)
        {
            // Диспетчер служб о ней не знает: остался только раздел.
            RemoveKey(new CleanupItem { Kind = CleanupItemKind.RegistryKey, Vendor = item.Vendor, Category = item.Category, Path = path }, backup);
            return true;
        }

        if (code != 0 && code != ErrorServiceMarkedForDelete)
        {
            throw new InvalidOperationException($"sc delete вернул код {code}");
        }

        using var still = SystemPrivileges.OpenReadable(Hklm, $@"SYSTEM\CurrentControlSet\Services\{item.Path}");
        return still is null ? true : null;
    }

    private static int RunSc(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("sc.exe", arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })
                            ?? throw new InvalidOperationException("sc.exe не запущен");
        process.StandardOutput.ReadToEnd();
        return process.WaitForExit(30_000) ? process.ExitCode : -1;
    }

    // ---------- Файлы ----------

    private static bool? RemoveFile(string path)
    {
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Файл занят (например, cpsspap.dll загружен в LSASS): удалится при перезагрузке, до запуска служб.
            return ScheduleDelete(path) ? null : throw new IOException($"файл занят, удаление при перезагрузке не назначено: {ex.Message}");
        }
    }

    /// <summary>Удаляет папку без перехода по ссылкам (junction/symlink удаляются как ссылки). null — часть удалится при перезагрузке.</summary>
    private static bool? RemoveDirectory(string path, bool allowContainers)
    {
        if (!Directory.Exists(path))
        {
            return true;
        }

        if (!LeftoverCleanup.IsSafeToDelete(path))
        {
            throw new InvalidOperationException("путь защищён от удаления");
        }

        // Контейнеры могли появиться после поиска: без подтверждения их папки не удаляем.
        if (!allowContainers && CryptoTraceScanner.FindContainerFolders(path).Count > 0)
        {
            throw new InvalidOperationException("в папке есть ключевые контейнеры");
        }

        return DeleteTree(new DirectoryInfo(path)) ? true : null;
    }

    private static bool DeleteTree(DirectoryInfo directory)
    {
        var complete = true;
        if ((directory.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo child)
                {
                    complete &= DeleteTree(child);
                }
                else
                {
                    complete &= RemoveFile(entry.FullName) == true;
                }
            }
        }

        try
        {
            directory.Attributes = FileAttributes.Directory;
            directory.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Пустой каталог удалится при перезагрузке после своих файлов: записи выполняются по порядку.
            if (!ScheduleDelete(directory.FullName))
            {
                throw;
            }

            complete = false;
        }

        return complete;
    }

    private static bool ScheduleDelete(string path) => MoveFileExW(path, null, MoveFileDelayUntilReboot);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileExW(string existingFileName, string? newFileName, uint flags);

    [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int BCryptRemoveContextFunction(uint table, string context, uint interfaceId, string function);

    [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int BCryptRemoveContextFunctionProvider(uint table, string context, uint interfaceId, string function, string provider);

    [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int BCryptUnregisterProvider(string provider);
}
