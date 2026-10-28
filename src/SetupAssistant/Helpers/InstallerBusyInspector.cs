using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SetupAssistant.Helpers;

/// <summary>Кто сейчас держит Windows Installer.</summary>
/// <param name="Description">Одна фраза для пользователя: какой процесс и что он делает.</param>
/// <param name="StartedByThisApp">Установку запустило это приложение (например, до отмены), она ещё завершается в фоне.</param>
/// <param name="WindowsUpdate">Идёт установка обновлений Windows.</param>
/// <param name="Pids">Номера найденных процессов (для диспетчера задач).</param>
public sealed record InstallerBusyCause(string Description, bool StartedByThisApp, bool WindowsUpdate, IReadOnlyList<int> Pids);

/// <summary>
/// Выясняет, чем занят Windows Installer. Мьютекс <c>_MSIExecute</c> говорит лишь «занят», а пользователю нужно знать, что именно
/// ставится или удаляется и кто это запустил: тогда понятно, ждать, закрывать программу или перезагружаться.
/// </summary>
public static class InstallerBusyInspector
{
    private static readonly Regex MsiOperation = new(
        @"(?:^|\s)[/-](?<op>i|package|x|uninstall|fa|fu|fm|fv|f)\s+(?<target>""[^""]+""|\{[0-9A-Fa-f\-]+\}|\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MsiService = new(@"\s[/-]V\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Описание того, что держит Windows Installer; null — определить не удалось (нет доступа к сведениям о процессах).</summary>
    public static InstallerBusyCause? Inspect()
    {
        try
        {
            var parts = new List<string>();
            var pids = new List<int>();
            var own = false;
            var update = false;

            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, Name, CommandLine, CreationDate FROM Win32_Process " +
                "WHERE Name = 'msiexec.exe' OR Name = 'TiWorker.exe' OR Name = 'wusa.exe'");
            foreach (ManagementObject process in searcher.Get())
            {
                using (process)
                {
                    var name = process["Name"] as string ?? string.Empty;
                    var commandLine = process["CommandLine"] as string ?? string.Empty;
                    var pid = Convert.ToInt32(process["ProcessId"]);
                    var parentPid = Convert.ToInt32(process["ParentProcessId"]);

                    if (name.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        // «msiexec /V» — служба Windows Installer, она работает всегда и ничего не устанавливает.
                        if (MsiService.IsMatch(commandLine) || commandLine.Contains("-Embedding", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var startedByUs = parentPid == Environment.ProcessId;
                        own |= startedByUs;
                        pids.Add(pid);
                        parts.Add($"msiexec.exe (PID {pid}{StartedAt(process)}) {DescribeMsi(commandLine)}{Launcher(parentPid, startedByUs)}");
                    }
                    else
                    {
                        update = true;
                        pids.Add(pid);
                        parts.Add($"{name} (PID {pid}{StartedAt(process)}) — установка обновлений Windows");
                    }
                }
            }

            return parts.Count == 0
                ? new InstallerBusyCause(
                    "процесс, который держит установщик, не найден (возможно, служба ещё завершает отменённую или прерванную операцию)",
                    StartedByThisApp: false, WindowsUpdate: false, pids)
                : new InstallerBusyCause(string.Join("; ", parts), own, update, pids);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Что делать, если установщик занят: зависит от того, кто его занял.</summary>
    public static string AdviceFor(InstallerBusyCause? cause)
    {
        if (cause is null)
        {
            return "Подождите одну-две минуты и повторите. Если установщик так и не освободился, перезагрузите компьютер.";
        }

        if (cause.StartedByThisApp)
        {
            return "Эту операцию запустило само приложение (например, до отмены): Windows Installer завершает её в фоне. " +
                   "Подождите несколько минут и повторите; если ничего не меняется 10–15 минут, перезагрузите компьютер.";
        }

        if (cause.WindowsUpdate)
        {
            return "Идёт установка обновлений Windows. Дождитесь её окончания («Параметры → Центр обновления Windows») или перезагрузите компьютер, затем повторите.";
        }

        if (cause.Pids.Count == 0)
        {
            return "Подождите одну-две минуты и нажмите «Проверить снова». Если не помогло, перезагрузите компьютер.";
        }

        return "Дождитесь окончания этой установки и повторите. Если она явно зависла (прошло больше 15 минут, диск не работает), " +
               $"завершите процесс msiexec.exe с указанным номером в диспетчере задач (PID {string.Join(", ", cause.Pids)}) или перезагрузите компьютер.";
    }

    private static string DescribeMsi(string commandLine)
    {
        var match = MsiOperation.Match(commandLine);
        if (!match.Success)
        {
            return "выполняет установку или удаление";
        }

        var op = match.Groups["op"].Value.ToLowerInvariant();
        var verb = op is "x" or "uninstall" ? "удаляет" : op.StartsWith('f') ? "восстанавливает" : "устанавливает";
        return $"{verb} {ResolveTarget(match.Groups["target"].Value)}";
    }

    private static string ResolveTarget(string target)
    {
        target = target.Trim('"');
        if (target.StartsWith('{'))
        {
            return ProductName(target) is { } name ? $"«{name}»" : target;
        }

        return $"«{Path.GetFileName(target)}»";
    }

    private static string? ProductName(string guid)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = baseKey.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{guid}");
            if (key?.GetValue("DisplayName") is string name && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return null;
    }

    private static string StartedAt(ManagementBaseObject process)
    {
        try
        {
            return process["CreationDate"] is string raw
                ? $", с {ManagementDateTimeConverter.ToDateTime(raw):HH:mm}"
                : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string Launcher(int parentPid, bool startedByUs)
    {
        if (startedByUs)
        {
            return ", запущена этим приложением";
        }

        try
        {
            using var parent = Process.GetProcessById(parentPid);
            return $", запустил {parent.ProcessName}.exe";
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
