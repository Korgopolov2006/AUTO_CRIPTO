using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using SetupAssistant.Models;

namespace SetupAssistant.Helpers;

/// <summary>
/// Резервная копия ключевых контейнеров КриптоПро перед полной очисткой: ветки реестра — в .reg (возвращаются двойным щелчком),
/// папки HDIMAGE — копией, сертификаты КриптоПро из «Личное» — в .cer. В копии закрытые ключи, поэтому доступ к папке отчёта
/// оставляется только администраторам, системе и текущему пользователю.
/// </summary>
public static class KeyContainerBackup
{
    private const string ContainersFolder = "containers";
    private const string CertificatesFolder = "certificates";

    /// <summary>Создаёт папку отчёта с ограниченным доступом.</summary>
    public static void PrepareDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            foreach (var sid in new[]
                     {
                         new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                         new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                         WindowsIdentity.GetCurrent().User!
                     })
            {
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
            }

            new DirectoryInfo(directory).SetAccessControl(security);
        }
        catch (Exception)
        {
            // Папка на диске без поддержки прав (FAT, сетевой ресурс) — копия всё равно нужна; предупреждение — в README.
        }
    }

    /// <summary>Копирует все контейнеры; true — скопированы все. В <paramref name="problems"/> — что не удалось.</summary>
    public static bool BackupContainers(IReadOnlyList<KeyContainerInfo> containers, string backupDirectory, List<string> problems)
    {
        if (containers.Count == 0)
        {
            return true;
        }

        var root = Path.Combine(backupDirectory, ContainersFolder);
        Directory.CreateDirectory(root);

        // Контейнеры в реестре: один .reg на раздел Keys (все контейнеры пользователя или компьютера).
        var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        var index = 0;
        foreach (var group in containers.Where(c => c.InRegistry).GroupBy(c => c.Location[..c.Location.LastIndexOf('\\')], StringComparer.OrdinalIgnoreCase))
        {
            var file = Path.Combine(root, $"registry-{++index}-{SafeName(group.First().Owner)}.reg");
            try
            {
                using var keys = SystemPrivileges.OpenReadable(hklm, group.Key["HKLM\\".Length..])
                                 ?? throw new IOException("раздел не найден");
                using (var writer = new RegFileWriter(file))
                {
                    writer.WriteComment($"Ключевые контейнеры КриптоПро ({group.First().Owner}). Вернуть: двойной щелчок по файлу от имени администратора.");
                    writer.WriteTree(RegFileWriter.ExpandHiveName(group.Key), keys);
                }

                var saved = File.ReadAllText(file, Encoding.Unicode);
                foreach (var container in group.Where(c => !saved.Contains($@"\Keys\{c.Name}]", StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add($"{container.Name}: не попал в копию реестра");
                }
            }
            catch (Exception ex)
            {
                problems.Add($"{group.Key}: {ex.Message}");
            }
        }

        // Контейнеры HDIMAGE: копия папки целиком, с проверкой числа и размера файлов.
        foreach (var container in containers.Where(c => !c.InRegistry))
        {
            var target = UniquePath(Path.Combine(root, "files", SafeName(container.Owner), Path.GetFileName(container.Location)));
            try
            {
                CopyDirectory(container.Location, target);
                var source = Directory.GetFiles(container.Location, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                var copy = Directory.GetFiles(target, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                if (source != copy)
                {
                    problems.Add($"{container.Name}: копия отличается от оригинала");
                }
            }
            catch (Exception ex)
            {
                problems.Add($"{container.Name}: {ex.Message}");
            }
        }

        return problems.Count == 0;
    }

    /// <summary>Сертификаты, привязанные к ключам КриптоПро («Личное» пользователя и компьютера), в .cer; возвращает их число.</summary>
    public static int ExportCertificates(string backupDirectory)
    {
        var exported = 0;
        var folder = Path.Combine(backupDirectory, CertificatesFolder);
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            try
            {
                using var store = new X509Store(StoreName.My, location);
                store.Open(OpenFlags.ReadOnly);
                foreach (var certificate in store.Certificates.Where(CertificateKeyProvider.IsCryptoPro))
                {
                    Directory.CreateDirectory(folder);
                    var name = $"{SafeName(certificate.GetNameInfo(X509NameType.SimpleName, false))}_{certificate.Thumbprint[..8]}.cer";
                    File.WriteAllBytes(Path.Combine(folder, name), certificate.Export(X509ContentType.Cert));
                    exported++;
                }
            }
            catch (Exception)
            {
                // Хранилище недоступно — сертификаты всё равно лежат в самих контейнерах или на токене.
            }
        }

        return exported;
    }

    /// <summary>Серийный номер КриптоПро CSP (ProductID в регистрации установщика); null — не зарегистрирован или не найден.</summary>
    public static string? ReadCryptoProSerial(string? productCode)
    {
        var packed = PackProductCode(productCode);
        if (packed is null)
        {
            return null;
        }

        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var properties = hklm.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products\{packed}\InstallProperties");
            return properties?.GetValue("ProductID") as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void WriteReadme(string backupDirectory)
    {
        var text = $"""
            Папка отчёта полной очистки СКЗИ ({DateTime.Now:dd.MM.yyyy HH:mm}).

            ВНИМАНИЕ: в папке containers — закрытые ключи электронной подписи. Храните папку так же, как сами ключи:
            не пересылайте по почте и не оставляйте на общих дисках. Доступ к папке ограничен администраторами и пользователем,
            запустившим очистку (если диск поддерживает права доступа).

            Состав:
              report.txt                 — что найдено, удалено, что удалится при перезагрузке и что не удалось
              registry\*.reg             — разделы и значения реестра до удаления (вернуть: двойной щелчок от имени администратора)
              containers\registry-*.reg  — ключевые контейнеры из реестра КриптоПро
              containers\files\          — ключевые контейнеры из папок (считыватель HDIMAGE)
              certificates\*.cer         — сертификаты, которые были привязаны к ключам КриптоПро

            Как вернуть контейнеры:
              1. Установите КриптоПро CSP.
              2. Контейнеры из реестра: двойной щелчок по containers\registry-*.reg от имени администратора.
              3. Контейнеры из папок: скопируйте папку контейнера (вида «имя.000») обратно в
                 C:\Users\<пользователь>\AppData\Local\Crypto Pro\ (или ту папку, откуда он был: она указана в report.txt).
              4. В КриптоПро CSP: Сервис → Просмотреть сертификаты в контейнере → Установить.
            """;
        File.WriteAllText(Path.Combine(backupDirectory, "README.txt"), text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>Код продукта {GUID} в «упакованном» виде ветки Installer (половинки групп переставлены).</summary>
    internal static string? PackProductCode(string? productCode)
    {
        if (productCode is null || !Guid.TryParse(productCode, out var guid))
        {
            return null;
        }

        var hex = guid.ToString("N").ToUpperInvariant();
        static string Reverse(string s) => new(s.Reverse().ToArray());
        var packed = new StringBuilder();
        packed.Append(Reverse(hex[..8])).Append(Reverse(hex[8..12])).Append(Reverse(hex[12..16]));
        for (var i = 16; i < 32; i += 2)
        {
            packed.Append(hex[i + 1]).Append(hex[i]);
        }

        return packed.ToString();
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    private static string UniquePath(string path)
    {
        var candidate = path;
        for (var i = 2; Directory.Exists(candidate); i++)
        {
            candidate = $"{path} ({i})";
        }

        return candidate;
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "_" : cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }
}
