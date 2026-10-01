using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IBrowserService"/>
public sealed class BrowserService : IBrowserService
{
    private const string KonturPluginDisplayNamePattern = "Контур.Плагин";
    private const string KonturPluginDisplayNamePatternAlt = "Kontur";
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

        var status = product is null
            ? StatusLevel.Error
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
        var product = _registryService.FindInstalledProduct(KonturPluginDisplayNamePattern)
                      ?? _registryService.FindInstalledProduct(KonturPluginDisplayNamePatternAlt);

        var status = _versionService.EvaluateStatus(product?.DisplayVersion, config.MinVersions.KonturPlugin, config.RecommendedVersions.KonturPlugin);

        return new ComponentInfo
        {
            Type = ComponentType.KonturPlugin,
            Name = "Контур.Плагин",
            InstalledVersion = product?.DisplayVersion,
            MinimumVersion = config.MinVersions.KonturPlugin,
            RecommendedVersion = config.RecommendedVersions.KonturPlugin,
            Description = "Плагин для работы с электронной подписью на порталах Контур",
            Status = status,
            ErrorMessage = status == StatusLevel.Error
                ? (product is null ? "Контур.Плагин не установлен" : $"Версия {product.DisplayVersion} ниже минимально допустимой {config.MinVersions.KonturPlugin}")
                : null
        };
    }
}
