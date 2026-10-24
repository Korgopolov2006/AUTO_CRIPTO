using System.IO;
using Microsoft.Win32;

namespace SetupAssistant.Helpers;

/// <summary>
/// Добивка пустых остатков после удаления. Правило везде одно — удаляется только то, в чём нет данных: папка без единого файла,
/// ветка реестра без единого значения (значит, в ней нет ни ключевых контейнеров, ни лицензии), ProgID без рабочего CLSID.
/// Поэтому добивка безопасна и для веток «Crypto Pro», которые основная очистка не трогает.
/// </summary>
public static class LeftoverCleanup
{
    /// <summary>Разделы, где даже пустая ветка может быть чужой: ни при каких настройках не трогаем.</summary>
    private static readonly string[] SharedVendors = { "Microsoft", "Classes", "Policies", "Google", "Mozilla" };

    /// <summary>Защита от опасных путей: корни дисков, системные и пользовательские каталоги целиком, слишком короткие пути.</summary>
    public static bool IsSafeToDelete(string path)
    {
        try
        {
            // Относительные пути и «C:foo» запрещены: GetFullPath привязал бы их к текущей папке процесса.
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                return false;
            }

            var full = Path.GetFullPath(path).TrimEnd('\\');
            var root = Path.GetPathRoot(full)?.TrimEnd('\\') ?? string.Empty;
            if (full.Length <= root.Length)
            {
                return false;
            }

            var depth = full[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
            if (depth < 2)
            {
                return false;
            }

            var protectedFolders = new[]
            {
                Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData,
                Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.MyDocuments
            }.Select(f => Environment.GetFolderPath(f).TrimEnd('\\'))
             .Append(Path.GetTempPath().TrimEnd('\\'))
             .Append(@"C:\Users");

            return !protectedFolders.Any(p => p.Length > 0 && string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>В папке и всех её подпапках нет ни одного файла (остались только пустые каталоги).</summary>
    public static bool IsEmptyFolderTree(string directory)
    {
        try
        {
            return Directory.Exists(directory) && !Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any();
        }
        catch (Exception)
        {
            // Нет доступа — считаем, что внутри могут быть данные.
            return false;
        }
    }

    /// <summary>У ветки и всех её подветок нет ни одного значения (в том числе «по умолчанию»).</summary>
    public static bool RegistryTreeHasValues(RegistryKey key)
    {
        try
        {
            if (key.ValueCount > 0)
            {
                return true;
            }

            foreach (var name in key.GetSubKeyNames())
            {
                using var child = key.OpenSubKey(name);
                if (child is null || RegistryTreeHasValues(child))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception)
        {
            // Подраздел закрыт для чтения (у КриптоПро так защищён «Random»): проверить нельзя, значит, считаем, что в нём данные.
            return true;
        }
    }

    /// <summary>
    /// Разбирает путь HKCU\… или HKLM\… глубиной не меньше двух сегментов; чужие разделы (Microsoft, Classes, Policies и т. п.)
    /// отклоняются. Узел WOW6432Node пропускается при определении производителя: HKLM\SOFTWARE\WOW6432Node\Crypto Pro допустим.
    /// </summary>
    public static bool TryParseBranch(string path, out RegistryKey hive, out string subKey)
    {
        hive = Registry.CurrentUser;
        subKey = string.Empty;

        var separator = path.IndexOf('\\');
        if (separator <= 0)
        {
            return false;
        }

        var prefix = path[..separator];
        if (prefix.Equals("HKCU", StringComparison.OrdinalIgnoreCase)) hive = Registry.CurrentUser;
        else if (prefix.Equals("HKLM", StringComparison.OrdinalIgnoreCase)) hive = Registry.LocalMachine;
        else return false;

        subKey = path[(separator + 1)..].Trim('\\');
        var segments = subKey.Split('\\', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count < 2 || !segments[0].Equals("SOFTWARE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var vendorIndex = segments[1].Equals("WOW6432Node", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        return segments.Count > vendorIndex && !SharedVendors.Contains(segments[vendorIndex], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ProgID — «осиротевший»: нет подраздела CLSID либо он указывает на класс, которого в системе нет. Такая запись ничего не запускает.
    /// </summary>
    public static bool IsOrphanProgId(RegistryKey progId)
    {
        try
        {
            using var clsidKey = progId.OpenSubKey("CLSID");
            if (clsidKey?.GetValue(null) is not string clsid || string.IsNullOrWhiteSpace(clsid))
            {
                return true;
            }

            return !ClassExists(clsid);
        }
        catch (Exception)
        {
            // Проверить нельзя — считаем запись рабочей и не удаляем.
            return false;
        }
    }

    private static bool ClassExists(string clsid)
    {
        foreach (var root in new[] { @"SOFTWARE\Classes\CLSID", @"SOFTWARE\WOW6432Node\Classes\CLSID" })
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{root}\{clsid}");
            if (key is not null)
            {
                return true;
            }
        }

        return false;
    }
}
