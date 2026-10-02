using System.Net.Http;
using System.Xml.Linq;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IExtensionUpdateService"/>
public sealed class ExtensionUpdateService : IExtensionUpdateService, IDisposable
{
    private const string UpdateEndpoint = "https://clients2.google.com/service/update2/crx";

    private readonly IConfigService _configService;
    private readonly IVersionService _versionService;
    private readonly ILoggingService _logger;
    private const string NoConnection = "нет связи с сервером обновлений";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public ExtensionUpdateService(IConfigService configService, IVersionService versionService, ILoggingService logger)
    {
        _configService = configService;
        _versionService = versionService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ExtensionUpdateInfo>> CheckAsync(CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(
            _configService.Current.Extensions.Items.Select(e => CheckOneAsync(e, cancellationToken)));

        var outdated = results.Count(r => r.IsUpdateAvailable);
        var unreachable = results.Count(r => r.Error == NoConnection);

        var summary = results.Length > 0 && unreachable == results.Length
            ? "Сервер обновлений недоступен (нет доступа в интернет)"
            : outdated == 0 ? "Все расширения актуальны" : $"Доступны обновления: {outdated}";
        _logger.Info("Проверка обновлений расширений", summary);

        return results;
    }

    private async Task<ExtensionUpdateInfo> CheckOneAsync(ExtensionDefinition extension, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(extension.StoreId))
        {
            return new ExtensionUpdateInfo(extension.Name, extension.Version, null, false, "не задан StoreId в Config.json");
        }

        try
        {
            var query = Uri.EscapeDataString($"id={extension.StoreId}&v={extension.Version}&uc");
            var url = $"{UpdateEndpoint}?response=updatecheck&os=win&arch=x64&prod=chromecrx&prodversion=140.0.0.0&acceptformat=crx3&x={query}";

            var xml = await _http.GetStringAsync(url, cancellationToken);
            XNamespace ns = "http://www.google.com/update2/response";
            var app = XDocument.Parse(xml).Descendants(ns + "app").FirstOrDefault();

            if (app?.Attribute("status")?.Value != "ok")
            {
                return new ExtensionUpdateInfo(extension.Name, extension.Version, null, false, "расширение не найдено в магазине");
            }

            var check = app.Element(ns + "updatecheck");
            var latest = check?.Attribute("version")?.Value;

            if (check?.Attribute("status")?.Value == "ok" && latest is not null
                && _versionService.Compare(latest, extension.Version) > 0)
            {
                return new ExtensionUpdateInfo(extension.Name, extension.Version, latest, true, null);
            }

            return new ExtensionUpdateInfo(extension.Name, extension.Version, latest ?? extension.Version, false, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new ExtensionUpdateInfo(extension.Name, extension.Version, null, false, NoConnection);
        }
    }

    public void Dispose() => _http.Dispose();
}
