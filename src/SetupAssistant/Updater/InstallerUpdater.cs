using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using SetupAssistant.Helpers;

namespace ExtensionUpdater;

/// <summary>
/// Сверяет дистрибутивы из раздела Installers с источниками в интернете и скачивает новые версии.
/// Файл принимается только с действительной подписью Authenticode (и издателем из ExpectedSigner, если задан).
/// </summary>
internal static class InstallerUpdater
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed record Installer(
        string Name, string Path, string DownloadUrl, string AssetRegex, string UpdateUrl, string WarmupUrl, string ExpectedSigner)
    {
        public string SourceUrl => !string.IsNullOrWhiteSpace(UpdateUrl) ? UpdateUrl : DownloadUrl;
    }

    private sealed record Remote(string FileName, long? Size, string Url, string? Referer, string? Version);

    private enum Verdict { UpToDate, NewVersion, SizeChanged, NoLocalFile }

    public static async Task<(string Text, int Updated, int Failed)> RunAsync(string appDir, string text, bool checkOnly, bool prune)
    {
        var updated = 0;
        var failed = 0;

        using var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");

        foreach (var installer in ReadInstallers(text))
        {
            if (string.IsNullOrWhiteSpace(installer.SourceUrl))
            {
                Console.WriteLine($"[без проверки] {installer.Name}: версия зафиксирована");
                continue;
            }

            try
            {
                var remote = await ResolveAsync(http, installer);
                var local = string.IsNullOrWhiteSpace(installer.Path) ? null : new FileInfo(System.IO.Path.Combine(appDir, installer.Path));
                if (local is { Exists: false })
                {
                    local = null;
                }

                var verdict = Compare(local, remote);
                var versionNote = remote.Version is null ? string.Empty : $" (версия {remote.Version})";

                if (verdict == Verdict.UpToDate)
                {
                    Console.WriteLine($"[актуально] {installer.Name}: {remote.FileName}");
                    continue;
                }

                Console.WriteLine(verdict switch
                {
                    Verdict.NewVersion => $"[новая версия] {installer.Name}: {local!.Name} -> {remote.FileName}{versionNote}",
                    Verdict.SizeChanged => $"[обновлён] {installer.Name}: размер {local!.Length} -> {remote.Size} байт ({remote.FileName})",
                    _ => $"[нет файла] {installer.Name}: {remote.FileName}{versionNote}"
                });

                if (checkOnly)
                {
                    continue;
                }

                var directory = local?.DirectoryName ?? System.IO.Path.Combine(appDir, "Appendices", installer.Name);
                var target = System.IO.Path.Combine(directory, remote.FileName);
                await DownloadAsync(http, remote, installer, target);

                var relative = System.IO.Path.GetRelativePath(appDir, target);
                text = SetPath(text, installer.Name, relative);
                updated++;
                Console.WriteLine($"    сохранено: {relative}{FileVersionNote(target)}");

                if (prune && verdict == Verdict.NewVersion && local is not null
                    && !string.Equals(local.FullName, target, StringComparison.OrdinalIgnoreCase))
                {
                    local.Delete();
                    Console.WriteLine($"    удалена старая версия: {local.Name}");
                }
                else if (verdict == Verdict.NewVersion && local is not null)
                {
                    Console.WriteLine($"    старый файл оставлен ({local.Name}); для удаления запустите с ключом --prune");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"[ошибка] {installer.Name}: {ex.Message}");
            }
        }

        return (text, updated, failed);
    }

    private static List<Installer> ReadInstallers(string configText)
    {
        using var doc = JsonDocument.Parse(configText, JsonOptions);
        if (!doc.RootElement.TryGetProperty("Installers", out var installers))
        {
            return new List<Installer>();
        }

        static string Get(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) ? v.GetString() ?? string.Empty : string.Empty;

        return installers.EnumerateObject()
            .Select(p => new Installer(
                p.Name, Get(p.Value, "Path"), Get(p.Value, "DownloadUrl"), Get(p.Value, "DownloadAssetRegex"),
                Get(p.Value, "UpdateUrl"), Get(p.Value, "UpdateWarmupUrl"), Get(p.Value, "ExpectedSigner")))
            .ToList();
    }

    private static async Task<Remote> ResolveAsync(HttpClient http, Installer installer)
    {
        if (!string.IsNullOrWhiteSpace(installer.AssetRegex))
        {
            var json = await http.GetStringAsync(installer.SourceUrl);
            using var doc = JsonDocument.Parse(json);
            var pattern = new Regex(installer.AssetRegex, RegexOptions.IgnoreCase);

            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                if (pattern.IsMatch(name))
                {
                    var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                    return new Remote(name, asset.GetProperty("size").GetInt64(), asset.GetProperty("browser_download_url").GetString()!, null, tag);
                }
            }

            throw new InvalidOperationException($"в последнем релизе нет файла под шаблон '{installer.AssetRegex}'");
        }

        string? referer = null;
        if (!string.IsNullOrWhiteSpace(installer.WarmupUrl))
        {
            referer = installer.WarmupUrl;
            using var warmup = await http.GetAsync(installer.WarmupUrl);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, installer.SourceUrl);
        if (referer is not null)
        {
            request.Headers.Referrer = new Uri(referer);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentType?.MediaType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidOperationException("сервер вернул страницу вместо файла (возможно, изменилась ссылка или нужен вход)");
        }

        var fileName = ReadFileName(response.Content.Headers);
        var finalUri = response.RequestMessage?.RequestUri ?? new Uri(installer.SourceUrl);
        fileName = System.IO.Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? finalUri.LocalPath : fileName);

        return new Remote(fileName, response.Content.Headers.ContentLength, finalUri.ToString(), referer, null);
    }

    /// <summary>Имя файла из Content-Disposition. Разбирается вручную: у КриптоПро заголовок оканчивается лишней «;», и .NET его отвергает.</summary>
    private static string? ReadFileName(System.Net.Http.Headers.HttpContentHeaders headers)
    {
        if (!headers.NonValidated.TryGetValues("Content-Disposition", out var values))
        {
            return null;
        }

        var match = Regex.Match(values.ToString(), "filename\\*?=(?:UTF-8'')?\"?([^\";]+)", RegexOptions.IgnoreCase);
        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value.Trim()) : null;
    }

    private static Verdict Compare(FileInfo? local, Remote remote)
    {
        if (local is null)
        {
            return Verdict.NoLocalFile;
        }

        if (!string.Equals(local.Name, remote.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return Verdict.NewVersion;
        }

        return remote.Size is { } size && size != local.Length ? Verdict.SizeChanged : Verdict.UpToDate;
    }

    private static async Task DownloadAsync(HttpClient http, Remote remote, Installer installer, string targetPath)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath)!);
        var partPath = targetPath + ".part";

        using var request = new HttpRequestMessage(HttpMethod.Get, remote.Url);
        if (remote.Referer is not null)
        {
            request.Headers.Referrer = new Uri(remote.Referer);
        }

        using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = File.Create(partPath);
            await source.CopyToAsync(target);
        }

        var signature = Authenticode.Verify(partPath, installer.ExpectedSigner);
        if (!signature.IsValid)
        {
            File.Delete(partPath);
            throw new InvalidDataException($"файл отвергнут: {signature.Message}");
        }

        Console.WriteLine($"    подпись: {signature.Signer}");
        File.Move(partPath, targetPath, overwrite: true);
    }

    private static string FileVersionNote(string path)
    {
        var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
        return string.IsNullOrWhiteSpace(version) ? string.Empty : $" (версия файла {version})";
    }

    /// <summary>Меняет только поле Path внутри блока дистрибутива, сохраняя комментарии в Config.json.</summary>
    private static string SetPath(string text, string installerName, string relativePath)
    {
        var block = Regex.Match(text, "\"" + Regex.Escape(installerName) + "\"\\s*:\\s*\\{[^{}]*\\}", RegexOptions.Singleline);
        if (!block.Success)
        {
            throw new InvalidOperationException("блок дистрибутива не найден в Config.json");
        }

        var jsonPath = relativePath.Replace(@"\", @"\\");
        var newBlock = Regex.Replace(block.Value, "(\"Path\"\\s*:\\s*\")[^\"]*(\")", m => m.Groups[1].Value + jsonPath + m.Groups[2].Value);

        var result = text.Remove(block.Index, block.Length).Insert(block.Index, newBlock);
        JsonDocument.Parse(result, JsonOptions).Dispose();
        return result;
    }
}
