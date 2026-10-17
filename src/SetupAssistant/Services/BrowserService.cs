using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IBrowserService"/>
public sealed class BrowserService : IBrowserService
{
    private static readonly string[] KonturPluginDisplayNamePatterns = { "Диаг.Плагин", "Контур.Плагин", "Diag.Plugin", "Kontur" };
    private const string GosuslugiPluginDisplayNamePattern = "Плагин пользователя систем электронного правительства";

    private readonly IRegistryService _registryService;
    private readonly IVersionService _versionService;
    private readonly IConfigService _configService;

    public BrowserService(IRegistryService registryService, IVersionService versionService, IConfigService configService)
    {
        _registryService = registryService;
        _versionService = versionService;
        _configService = configService;
    }

    public ComponentInfo CheckBrowser(BrowserDefinition browser)
    {
        var config = _configService.Current;
        var product = _registryService.FindInstalledProduct(browser.RegistryDisplayNamePattern);

        var isGost = browser.Kind == BrowserKind.ChromiumGost;
        var minVersion = isGost ? config.MinVersions.ChromiumGost : null;
        var recommendedVersion = isGost ? config.RecommendedVersions.ChromiumGost : null;

        // Обязателен только браузер с поддержкой ГОСТ (Chromium GOST); остальные — по желанию, их отсутствие не ошибка.
        var missingStatus = browser.RequiresGostSupport ? StatusLevel.Error : StatusLevel.NotChecked;

        var status = product is null
            ? missingStatus
            : (isGost
                ? _versionService.EvaluateStatus(product.DisplayVersion, minVersion, recommendedVersion)
                : StatusLevel.Ok);

        return new ComponentInfo
        {
            Type = ComponentType.ChromiumGost,
            Name = browser.Name,
            InstalledVersion = product?.DisplayVersion,
            MinimumVersion = minVersion,
            RecommendedVersion = recommendedVersion,
            Description = browser.RequiresGostSupport
                ? "Браузер с поддержкой ГОСТ-шифрования"
                : product is null
                    ? "Не установлен — не требуется"
                    : "Браузер, совместимый с расширениями электронной подписи",
            Status = status,
            ErrorMessage = status == StatusLevel.Error
                ? (product is null ? $"{browser.Name} не установлен" : $"Версия {product.DisplayVersion} ниже минимально допустимой {minVersion}")
                : null
        };
    }

    public IReadOnlyList<ComponentInfo> CheckAllBrowsers() =>
        _configService.Current.SupportedBrowsers.Select(CheckBrowser).ToList();

    public ComponentInfo CheckGosuslugiPlugin()
    {
        var product = _registryService.FindInstalledProduct(GosuslugiPluginDisplayNamePattern);

        return new ComponentInfo
        {
            Type = ComponentType.GosuslugiPlugin,
            Name = "Плагин Госуслуг",
            InstalledVersion = product?.DisplayVersion,
            Description = "Плагин пользователя систем электронного правительства (для входа и подписи на Госуслугах)",
            Status = product is null ? StatusLevel.Error : StatusLevel.Ok,
            ErrorMessage = product is null ? "Плагин Госуслуг не установлен" : null
        };
    }

    public ComponentInfo CheckKonturPlugin()
    {
        var config = _configService.Current;
        var product = KonturPluginDisplayNamePatterns
            .Select(_registryService.FindInstalledProduct)
            .FirstOrDefault(p => p is not null);

        // Установщик Контура ставит плагин для текущего пользователя и называет его «Диаг.Плагин»; версия есть и в HKCU\SOFTWARE\SKBKontur.
        var installedVersion = product?.DisplayVersion
                               ?? _registryService.ReadValue("HKCU", @"SOFTWARE\SKBKontur\DiagPlugin", "Version");

        var status = _versionService.EvaluateStatus(installedVersion, config.MinVersions.KonturPlugin, config.RecommendedVersions.KonturPlugin);

        return new ComponentInfo
        {
            Type = ComponentType.KonturPlugin,
            Name = "Контур.Плагин",
            InstalledVersion = installedVersion,
            MinimumVersion = config.MinVersions.KonturPlugin,
            RecommendedVersion = config.RecommendedVersions.KonturPlugin,
            Description = "Плагин для работы с электронной подписью на порталах Контур",
            Status = status,
            ErrorMessage = status == StatusLevel.Error
                ? (installedVersion is null ? "Контур.Плагин не установлен" : $"Версия {installedVersion} ниже минимально допустимой {config.MinVersions.KonturPlugin}")
                : null
        };
    }
}
