using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ExtensionUpdater;

/// <summary>
/// Сверяет расширения из Config\Config.json готового приложения с Chrome Web Store, скачивает
/// более новые .crx в Appendices и правит Config.json (Id, CrxPath, Version, без PemPath).
/// </summary>
internal static class Updater
{
    private const string Endpoint = "https://clients2.google.com/service/update2/crx";
    private static readonly XNamespace Ns = "http://www.google.com/update2/response";
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed record Extension(string Name, string StoreId, string Version, string CrxPath);

    private sealed record Latest(string Version, string Codebase, string? Sha256);

    public static async Task<int> RunAsync(string appDir, bool checkOnly, bool prune, bool askToPrune)
    {
        var configPath = Path.Combine(appDir, "Config", "Config.json");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Не найден файл {configPath}");
            Console.Error.WriteLine("Положите ExtensionUpdater.exe рядом с SetupAssistant.exe или передайте путь к папке приложения первым аргументом.");
            return 2;
        }

        var text = File.ReadAllText(configPath);
        var extensions = ReadExtensions(text);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SetupAssistant-ExtensionUpdater/1.0");

        Console.WriteLine($"Папка приложения: {appDir}");
        Console.WriteLine(checkOnly ? "Режим: только проверка" : "Режим: проверка и загрузка обновлений");
        Console.WriteLine();

        var updated = 0;
        var failed = 0;

        foreach (var extension in extensions)
        {
            if (string.IsNullOrWhiteSpace(extension.StoreId))
            {
                Console.WriteLine($"[пропуск] {extension.Name}: не задан StoreId");
                continue;
            }

            try
            {
                var latest = await QueryLatestAsync(http, extension);
                if (latest is null)
                {
                    Console.WriteLine($"[актуально] {extension.Name} {extension.Version}");
                    continue;
                }

                Console.WriteLine($"[новая версия] {extension.Name}: {extension.Version} -> {latest.Version}");
                if (checkOnly)
                {
                    continue;
                }

                var relativePath = BuildRelativePath(extension, latest.Version);
                await DownloadAsync(http, latest, Path.Combine(appDir, relativePath));
                text = ApplyToConfig(text, extension, latest.Version, relativePath);
                updated++;
                Console.WriteLine($"    сохранено: {relativePath}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"[ошибка] {extension.Name}: {ex.Message}");
            }
        }

        Console.WriteLine();
        if (updated > 0)
        {
            File.Copy(configPath, configPath + ".bak", overwrite: true);
            File.WriteAllText(configPath, text, Utf8);
            Console.WriteLine($"Обновлено расширений: {updated}. Config.json изменён (копия: Config.json.bak).");
        }
        else if (!checkOnly && failed == 0)
        {
            Console.WriteLine("Обновлений нет.");
        }

        failed += HandleStaleFiles(appDir, ReadExtensions(text), checkOnly, prune, askToPrune);

        return failed > 0 ? 1 : 0;
    }

    /// <summary>
    /// Старые версии — это лишние .crx в папке каждого расширения. Файл из конфига никогда не удаляется,
    /// а если он отсутствует, папка не трогается. .pem не удаляются: это ключи для перепаковки.
    /// </summary>
    private static List<FileInfo> FindStaleFiles(string appDir, IReadOnlyList<Extension> extensions)
    {
        var referenced = extensions
            .Where(e => !string.IsNullOrWhiteSpace(e.CrxPath))
            .Select(e => Path.GetFullPath(Path.Combine(appDir, e.CrxPath)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stale = new List<FileInfo>();
        foreach (var current in referenced.Where(File.Exists))
        {
            foreach (var file in new DirectoryInfo(Path.GetDirectoryName(current)!).GetFiles("*.crx"))
            {
                if (!referenced.Contains(file.FullName))
                {
                    stale.Add(file);
                }
            }
        }

        return stale;
    }

    private static int HandleStaleFiles(string appDir, IReadOnlyList<Extension> extensions, bool checkOnly, bool prune, bool askToPrune)
    {
        var stale = FindStaleFiles(appDir, extensions);
        if (stale.Count == 0)
        {
            return 0;
        }

        var sizeMb = stale.Sum(f => f.Length) / 1024.0 / 1024.0;
        Console.WriteLine($"Старые версии расширений: {stale.Count} файл(ов), {sizeMb:F1} МБ");
        foreach (var file in stale)
        {
            Console.WriteLine($"    {Path.GetRelativePath(appDir, file.FullName)}");
        }

        if (checkOnly)
        {
            Console.WriteLine("Чтобы удалить, запустите с ключом --prune.");
            return 0;
        }

        if (!prune)
        {
            if (!askToPrune)
            {
                Console.WriteLine("Чтобы удалить, запустите с ключом --prune.");
                return 0;
            }

            Console.Write("Удалить эти файлы? [y/N]: ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }
        }

        var failed = 0;
        foreach (var file in stale)
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"[ошибка] не удалось удалить {file.Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"Удалено файлов: {stale.Count - failed}");
        return failed;
    }

    private static List<Extension> ReadExtensions(string configText)
    {
        using var doc = JsonDocument.Parse(configText, JsonOptions);
        var items = doc.RootElement.GetProperty("Extensions").GetProperty("Items");

        static string Get(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) ? v.GetString() ?? string.Empty : string.Empty;

        return items.EnumerateArray()
            .Select(e => new Extension(Get(e, "Name"), Get(e, "StoreId"), Get(e, "Version"), Get(e, "CrxPath")))
            .ToList();
    }

    private static async Task<Latest?> QueryLatestAsync(HttpClient http, Extension extension)
    {
        var query = Uri.EscapeDataString($"id={extension.StoreId}&v={extension.Version}&uc");
        var url = $"{Endpoint}?response=updatecheck&os=win&arch=x64&prod=chromecrx&prodversion=140.0.0.0&acceptformat=crx3&x={query}";

        var xml = await http.GetStringAsync(url);
        var app = XDocument.Parse(xml).Descendants(Ns + "app").FirstOrDefault();
        if (app?.Attribute("status")?.Value != "ok")
        {
            throw new InvalidOperationException("расширение не найдено в магазине");
        }

        var check = app.Element(Ns + "updatecheck");
        var version = check?.Attribute("version")?.Value;
        var codebase = check?.Attribute("codebase")?.Value;

        if (check?.Attribute("status")?.Value != "ok" || version is null || codebase is null
            || !Version.TryParse(version, out var newVersion)
            || !Version.TryParse(extension.Version, out var currentVersion)
            || newVersion <= currentVersion)
        {
            return null;
        }

        return new Latest(version, codebase, check.Attribute("hash_sha256")?.Value);
    }

    private static string BuildRelativePath(Extension extension, string version)
    {
        var parts = extension.CrxPath.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var folder = parts.Length >= 2 ? parts[^2] : extension.Name;
        return $@"Appendices\расширения chromium\{folder}\{version}_0.crx";
    }

    private static async Task DownloadAsync(HttpClient http, Latest latest, string targetPath)
    {
        if (!latest.Codebase.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("сервер вернул небезопасную ссылку на файл");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var partPath = targetPath + ".part";

        using (var response = await http.GetAsync(latest.Codebase, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = File.Create(partPath);
            await source.CopyToAsync(target);
        }

        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(partPath)));
        if (!string.IsNullOrEmpty(latest.Sha256) && !hash.Equals(latest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partPath);
            throw new InvalidDataException("контрольная сумма SHA-256 не совпала, файл удалён");
        }

        File.Move(partPath, targetPath, overwrite: true);
    }

    /// <summary>Правит блок расширения точечной заменой текста, чтобы сохранить комментарии в Config.json.</summary>
    private static string ApplyToConfig(string text, Extension extension, string newVersion, string relativePath)
    {
        var match = Regex.Match(text, @"\{\s*""Name"":\s*""" + Regex.Escape(extension.Name) + @""".*?\}", RegexOptions.Singleline);
        if (!match.Success)
        {
            throw new InvalidOperationException("блок расширения не найден в Config.json");
        }

        static string SetValue(string block, string property, string value) =>
            Regex.Replace(block, $@"(""{property}"":\s*"")[^""]*("")", m => m.Groups[1].Value + value + m.Groups[2].Value);

        var block = match.Value;
        block = SetValue(block, "Id", extension.StoreId);
        block = SetValue(block, "CrxPath", relativePath.Replace(@"\", @"\\"));
        block = SetValue(block, "Version", newVersion);
        block = Regex.Replace(block, @"\s*""PemPath"":\s*""[^""]*"",", string.Empty);
        block = Regex.Replace(block, @",\s*""PemPath"":\s*""[^""]*""(?=\s*\})", string.Empty);

        var result = text.Remove(match.Index, match.Length).Insert(match.Index, block);
        JsonDocument.Parse(result, JsonOptions).Dispose();
        return result;
    }
}
