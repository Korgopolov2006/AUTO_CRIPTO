using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SetupAssistant.Helpers;

/// <summary>Программа или служба, которая держит файлы наших компонентов.</summary>
/// <param name="Closable">Её можно попросить закрыться (у неё есть окно); службы и оболочку Windows не закрываем.</param>
public sealed record LockingApp(int ProcessId, string Name, string? ServiceName, bool Closable)
{
    public string Display => ServiceName is { Length: > 0 } && !Closable
        ? $"служба «{Name}»"
        : $"{Name} (PID {ProcessId})";
}

/// <summary>
/// Выясняет через Windows Restart Manager, какие программы держат файлы КриптоПро, Рутокен, плагинов и Континент-АП.
/// Так же поступают установщики Inno Setup и Windows Installer, когда просят «закрыть приложения». Видны только программы,
/// которые держат файл открытым или загруженным прямо сейчас (например, браузер с подключённым плагином).
/// </summary>
public static class FileLockInspector
{
    private const int ErrorMoreData = 234;
    private const int MaxFiles = 800;
    private static readonly string[] Extensions = { ".dll", ".exe", ".ocx" };

    /// <summary>Папки компонентов, которые ставит и удаляет приложение (допускаются переменные среды).</summary>
    private static readonly string[] ManagedFolders =
    {
        @"%ProgramFiles%\Crypto Pro",
        @"%ProgramFiles(x86)%\Crypto Pro",
        @"%CommonProgramFiles%\Crypto Pro",
        @"%CommonProgramFiles(x86)%\Crypto Pro",
        @"%ProgramFiles%\Aktiv Co",
        @"%ProgramFiles(x86)%\Aktiv Co",
        @"%ProgramFiles%\Security Code",
        @"%ProgramFiles(x86)%\Security Code",
        @"%LOCALAPPDATA%\SkbKontur",
        @"%APPDATA%\Rostelecom\IFCPlugin"
    };

    /// <summary>Программы, держащие файлы компонентов; пусто, если таких нет или проверить не удалось.</summary>
    public static IReadOnlyList<LockingApp> FindForManagedComponents() => Find(EnumerateManagedFiles());

    /// <summary>Программы, держащие перечисленные файлы.</summary>
    public static IReadOnlyList<LockingApp> Find(IReadOnlyCollection<string> files)
    {
        if (files.Count == 0)
        {
            return Array.Empty<LockingApp>();
        }

        var key = new StringBuilder(33);
        if (RmStartSession(out var session, 0, key) != 0)
        {
            return Array.Empty<LockingApp>();
        }

        try
        {
            var names = files.ToArray();
            if (RmRegisterResources(session, (uint)names.Length, names, 0, null, 0, null) != 0)
            {
                return Array.Empty<LockingApp>();
            }

            uint needed = 0;
            uint count = 0;
            uint reasons = 0;
            var result = RmGetList(session, out needed, ref count, null, ref reasons);
            for (var attempt = 0; attempt < 3 && result == ErrorMoreData; attempt++)
            {
                var info = new RmProcessInfo[needed];
                count = needed;
                result = RmGetList(session, out needed, ref count, info, ref reasons);
                if (result == 0)
                {
                    return info.Take((int)count).Select(ToApp).Where(a => a is not null).Select(a => a!)
                        .GroupBy(a => a.ProcessId).Select(g => g.First()).ToList();
                }
            }

            return Array.Empty<LockingApp>();
        }
        catch (Exception)
        {
            return Array.Empty<LockingApp>();
        }
        finally
        {
            RmEndSession(session);
        }
    }

    private static LockingApp? ToApp(RmProcessInfo info)
    {
        var pid = info.Process.ProcessId;
        if (pid == Environment.ProcessId || pid <= 4)
        {
            return null;
        }

        // 1 — окно, 2 — другое окно, 5 — консоль; 3 — служба, 4 — проводник, 1000 — критичный процесс Windows.
        var closable = info.ApplicationType is 1 or 2 or 5;
        var name = string.IsNullOrWhiteSpace(info.AppName) ? $"процесс {pid}" : info.AppName;
        return new LockingApp(pid, name, string.IsNullOrWhiteSpace(info.ServiceShortName) ? null : info.ServiceShortName, closable);
    }

    private static List<string> EnumerateManagedFiles()
    {
        var files = new List<string>();
        foreach (var folder in ManagedFolders)
        {
            if (files.Count >= MaxFiles)
            {
                break;
            }

            try
            {
                var path = Environment.ExpandEnvironmentVariables(folder);
                if (!Directory.Exists(path))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    {
                        files.Add(file);
                        if (files.Count >= MaxFiles)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Нет доступа к папке — её файлы просто не проверяются.
            }
        }

        return files;
    }

    /// <summary>
    /// Вежливо просит программы закрыться (как кнопка «закрыть» в окне: пользователь увидит вопрос о несохранённом) и ждёт.
    /// Возвращает номера процессов, которые остались.
    /// </summary>
    public static async Task<IReadOnlyList<int>> RequestCloseAsync(IEnumerable<int> processIds, TimeSpan wait)
    {
        var processes = new List<Process>();
        foreach (var pid in processIds.Distinct())
        {
            try
            {
                var process = Process.GetProcessById(pid);
                process.CloseMainWindow();
                processes.Add(process);
            }
            catch (Exception)
            {
                // Процесс уже завершился.
            }
        }

        var deadline = DateTime.UtcNow + wait;
        while (DateTime.UtcNow < deadline && processes.Any(p => !p.HasExited))
        {
            await Task.Delay(300);
        }

        var left = processes.Where(p => !p.HasExited).Select(p => p.Id).ToList();
        processes.ForEach(p => p.Dispose());
        return left;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int ProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string ServiceShortName;

        public int ApplicationType;
        public uint AppStatus;
        public uint TsSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, StringBuilder sessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint sessionHandle, uint fileCount, string[]? fileNames,
        uint applicationCount, RmUniqueProcess[]? applications, uint serviceCount, string[]? serviceNames);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(uint sessionHandle, out uint procInfoNeeded, ref uint procInfo,
        [In, Out] RmProcessInfo[]? affectedApps, ref uint rebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);
}
