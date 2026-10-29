using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IRemoteVersionService"/>
/// <remarks>
/// Те же источники, что у утилиты обновления дистрибутивов (UpdateUrl / DownloadUrl в Config.json), но без скачивания файлов:
/// читаются только заголовки ответа. Номер версии есть не у каждого сервера, поэтому для остальных возвращается имя,
/// размер и дата файла — по ним видно, что файл на сервере изменился.
/// </remarks>
public sealed class RemoteVersionService : IRemoteVersionService, IDisposable
{
    private const string NoConnection = "нет связи с сервером";

    private static readonly Regex VersionInName = new(@"(\d+(?:\.\d+){2,})", RegexOptions.Compiled);

    private readonly IConfigService _configService;
    private readonly ILoggingService _logger;
    private readonly HttpClient _http;

    public RemoteVersionService(IConfigService configService, ILoggingService logger)
    {
        _configService = configService;
        _logger = logger;

        _http = new HttpClient(new HttpClientHandler { CookieContainer = new System.Net.CookieContainer(), UseCookies = true })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
    }

    public async Task<IReadOnlyList<RemoteDistribution>> CheckAsync(CancellationToken cancellationToken = default)
    {
        var installers = _configService.Current.Installers;
        var items = new (string Key, string Title, InstallerDefinition Definition)[]
        {
            ("CryptoProCsp", "КриптоПро CSP", installers.CryptoProCsp),
            ("CryptoProBrowserPlugin", "КриптоПро ЭЦП Browser plug-in", installers.CryptoProBrowserPlugin),
            ("ChromiumGost", "Chromium GOST", installers.ChromiumGost),
            ("KonturPlugin", "Контур.Плагин", installers.KonturPlugin),
            ("GosuslugiPlugin", "Плагин Госуслуг", installers.GosuslugiPlugin),
            ("RutokenDriver", "Драйверы Рутокен", installers.RutokenDriver)
        };

        var results = await Task.WhenAll(items.Select(i => CheckOneAsync(i.Key, i.Title, i.Definition, cancellationToken)));

        var failed = results.Count(r => r.Error is not null);
        _logger.Info("Проверка актуальных версий", failed == 0 ? "Серверы ответили" : $"Не удалось проверить: {failed}");
        return results;
    }

    private async Task<RemoteDistribution> CheckOneAsync(string key, string title, InstallerDefinition definition, CancellationToken cancellationToken)
    {
        var url = !string.IsNullOrWhiteSpace(definition.UpdateUrl) ? definition.UpdateUrl : definition.DownloadUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return new RemoteDistribution(key, title, null, null, null, null, null, IsFixed: true);
        }

        try
        {
            return string.IsNullOrWhiteSpace(definition.DownloadAssetRegex)
                ? await ReadHeadersAsync(key, title, definition, url, cancellationToken)
                : await ReadGitHubReleaseAsync(key, title, definition, url, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new RemoteDistribution(key, title, null, null, null, null, NoConnection);
        }
        catch (Exception ex)
        {
            _logger.Warning("Проверка актуальных версий", $"{title}: {ex.Message}");
            return new RemoteDistribution(key, title, null, null, null, null, ex.Message);
        }
    }

    private async Task<RemoteDistribution> ReadGitHubReleaseAsync(
        string key, string title, InstallerDefinition definition, string url, CancellationToken cancellationToken)
    {
        var json = await _http.GetStringAsync(url, cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var pattern = new Regex(definition.DownloadAssetRegex, RegexOptions.IgnoreCase);

        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (!pattern.IsMatch(name))
            {
                continue;
            }

            var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var version = (tag ?? VersionInName.Match(name).Value).TrimStart('v', 'V');
            DateTimeOffset? published = doc.RootElement.TryGetProperty("published_at", out var p) && p.TryGetDateTimeOffset(out var date) ? date : null;
            return new RemoteDistribution(key, title, string.IsNullOrWhiteSpace(version) ? null : version, name,
                asset.GetProperty("size").GetInt64(), published, null);
        }

        throw new InvalidOperationException("в последнем релизе нет файла нужного вида");
    }

    private async Task<RemoteDistribution> ReadHeadersAsync(
        string key, string title, InstallerDefinition definition, string url, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(definition.UpdateWarmupUrl))
        {
            using var warmup = await _http.GetAsync(definition.UpdateWarmupUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        // Просим один байт: заголовки приходят те же, а файл целиком не качается.
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(0, 0);
        if (!string.IsNullOrWhiteSpace(definition.UpdateWarmupUrl))
        {
            request.Headers.Referrer = new Uri(definition.UpdateWarmupUrl);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentType?.MediaType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidOperationException("сервер вернул страницу вместо файла (возможно, изменилась ссылка)");
        }

        var finalUri = response.RequestMessage?.RequestUri ?? new Uri(url);
        var fileName = ReadFileName(response.Content.Headers) ?? Path.GetFileName(Uri.UnescapeDataString(finalUri.LocalPath));
        var size = response.Content.Headers.ContentRange?.Length ?? response.Content.Headers.ContentLength;
        var version = VersionInName.Match(fileName).Success ? VersionInName.Match(fileName).Value : null;

        return new RemoteDistribution(key, title, version, fileName, size, response.Content.Headers.LastModified, null);
    }

    /// <summary>Имя файла из Content-Disposition разбирается вручную: у КриптоПро заголовок оканчивается лишней «;».</summary>
    private static string? ReadFileName(HttpContentHeaders headers)
    {
        if (!headers.NonValidated.TryGetValues("Content-Disposition", out var values))
        {
            return null;
        }

        var match = Regex.Match(values.ToString(), "filename\\*?=(?:UTF-8'')?\"?([^\";]+)", RegexOptions.IgnoreCase);
        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value.Trim()) : null;
    }

    public void Dispose() => _http.Dispose();
}
