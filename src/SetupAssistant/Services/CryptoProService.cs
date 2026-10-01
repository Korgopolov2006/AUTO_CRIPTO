using SetupAssistant.Helpers;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="ICryptoProService"/>
public sealed class CryptoProService : ICryptoProService
{
    private const uint ProviderTypeGost2001 = 75;
    private const uint ProviderTypeGost2012_256 = 80;
    private const uint ProviderTypeGost2012_512 = 81;

    private static readonly TimeSpan CsptestQueryTimeout = TimeSpan.FromSeconds(30);

    private static readonly string[] KontinentApPatterns = { "Континент-АП", "Континент АП", "Continent-AP", "Continent AP" };

    private const string CspDisplayNamePattern = "КриптоПро CSP";
    private const string CspDisplayNamePatternEn = "CryptoPro CSP";
    private const string BrowserPluginDisplayNamePattern = "CryptoPro Browser plug-in";
    private const string BrowserPluginDisplayNamePatternAlt = "КриптоПро ЭЦП Browser plug-in";

    private readonly IRegistryService _registryService;
    private readonly IVersionService _versionService;
    private readonly IConfigService _configService;

    public CryptoProService(IRegistryService registryService, IVersionService versionService, IConfigService configService)
    {
        _registryService = registryService;
        _versionService = versionService;
        _configService = configService;
    }

    public ComponentInfo CheckCsp()
    {
        var config = _configService.Current;
        var product = _registryService.FindInstalledProduct(CspDisplayNamePattern)
                      ?? _registryService.FindInstalledProduct(CspDisplayNamePatternEn);

        return BuildComponentInfo(
            ComponentType.CryptoProCsp,
            "КриптоПро CSP",
            product?.DisplayVersion,
            config.MinVersions.CryptoProCsp,
            config.RecommendedVersions.CryptoProCsp,
            "Криптопровайдер для работы с электронной подписью");
    }

    public ComponentInfo CheckBrowserPlugin()
    {
        var config = _configService.Current;
        var product = _registryService.FindInstalledProduct(BrowserPluginDisplayNamePattern)
                      ?? _registryService.FindInstalledProduct(BrowserPluginDisplayNamePatternAlt);

        return BuildComponentInfo(
            ComponentType.CryptoProBrowserPlugin,
            "КриптоПро Browser Plugin",
            product?.DisplayVersion,
            config.MinVersions.CryptoProBrowserPlugin,
            config.RecommendedVersions.CryptoProBrowserPlugin,
            "Плагин интеграции браузера с КриптоПро CSP");
    }

    public ComponentInfo CheckProviders()
    {
        var providers = CryptoProviders.Enumerate()
            .Where(p => p.Name.Contains("Crypto-Pro", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains("КриптоПро", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var hasGost2012 = providers.Any(p => p.Type is ProviderTypeGost2012_256 or ProviderTypeGost2012_512);
        var hasGost2001 = providers.Any(p => p.Type == ProviderTypeGost2001);

        var status = hasGost2012 ? StatusLevel.Ok : providers.Count > 0 ? StatusLevel.Warning : StatusLevel.Error;

        return new ComponentInfo
        {
            Type = ComponentType.CryptoProProviders,
            Name = "Провайдеры КриптоПро",
            Description = providers.Count == 0
                ? "Криптопровайдеры КриптоПро в Windows не зарегистрированы"
                : $"Зарегистрировано: {string.Join("; ", providers.Select(p => $"{p.Name} (тип {p.Type})"))}",
            Status = status,
            ErrorMessage = status switch
            {
                StatusLevel.Error => "Провайдеры КриптоПро CSP не найдены: установите КриптоПро CSP",
                StatusLevel.Warning => hasGost2001
                    ? "Есть только провайдер ГОСТ Р 34.10-2001; для действующих сертификатов нужен ГОСТ Р 34.10-2012"
                    : "Не найден провайдер ГОСТ Р 34.10-2012 (типы 80/81)",
                _ => null
            }
        };
    }

    public ComponentInfo CheckContainers()
    {
        var info = new ComponentInfo
        {
            Type = ComponentType.CryptoProContainers,
            Name = "Ключевые контейнеры КриптоПро",
            Description = "Контейнеры закрытых ключей на токенах, смарт-картах и в реестре"
        };

        var csptest = CsptestRunner.FindPath();
        if (csptest is null)
        {
            info.Status = StatusLevel.Warning;
            info.ErrorMessage = "csptest.exe не найден: КриптоПро CSP не установлен";
            return info;
        }

        var containers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var anySucceeded = false;

        foreach (var providerType in new[] { ProviderTypeGost2012_256, ProviderTypeGost2012_512, ProviderTypeGost2001 })
        {
            var run = CsptestRunner.Run(
                csptest,
                $"-keyset -enum_containers -fqcn -verifycontext -silent -provtype {providerType}",
                CsptestQueryTimeout);

            if (run.TimedOut || run.ExitCode != 0)
            {
                continue;
            }

            anySucceeded = true;
            foreach (var line in run.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.TrimStart().StartsWith(@"\\.\", StringComparison.Ordinal))
                {
                    containers.Add(line.Trim());
                }
            }
        }

        if (!anySucceeded)
        {
            info.Status = StatusLevel.Warning;
            info.ErrorMessage = "Не удалось получить список контейнеров: проверьте регистрацию провайдера КриптоПро";
        }
        else if (containers.Count == 0)
        {
            info.Status = StatusLevel.Warning;
            info.ErrorMessage = "Контейнеры не найдены: подключите носитель с ключом электронной подписи";
        }
        else
        {
            info.Status = StatusLevel.Ok;
            info.InstalledVersion = $"{containers.Count} шт.";
            info.Description = $"Найдено контейнеров: {containers.Count}. {string.Join("; ", containers.Take(5))}{(containers.Count > 5 ? "…" : string.Empty)}";
        }

        return info;
    }

    public ComponentInfo CheckReaders()
    {
        var info = new ComponentInfo
        {
            Type = ComponentType.CryptoProReaders,
            Name = "Считыватели КриптоПро",
            Description = "Считыватели смарт-карт, доступные криптопровайдеру (отдельно от USB-драйвера токена)"
        };

        var csptest = CsptestRunner.FindPath();
        if (csptest is null)
        {
            info.Status = StatusLevel.Warning;
            info.ErrorMessage = "csptest.exe не найден: КриптоПро CSP не установлен";
            return info;
        }

        var run = CsptestRunner.Run(csptest, "-card -enum", CsptestQueryTimeout);
        if (run.TimedOut || run.ExitCode != 0)
        {
            info.Status = StatusLevel.Warning;
            info.ErrorMessage = run.TimedOut
                ? "Превышено время ожидания при получении списка считывателей"
                : $"Не удалось получить список считывателей (код {run.ExitCode})";
            return info;
        }

        var readers = run.Output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("Total:", StringComparison.OrdinalIgnoreCase)
                        && !l.StartsWith("[ErrorCode", StringComparison.OrdinalIgnoreCase)
                        && !l.Contains("csptest", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (readers.Count == 0)
        {
            info.Status = StatusLevel.Warning;
            info.ErrorMessage = "Считыватели не обнаружены: подключите носитель или проверьте службу «Смарт-карты»";
        }
        else
        {
            info.Status = StatusLevel.Ok;
            info.Description = $"Считыватели: {string.Join("; ", readers.Take(4))}{(readers.Count > 4 ? "…" : string.Empty)}";
        }

        return info;
    }

    public ComponentInfo CheckKontinentAp()
    {
        var product = KontinentApPatterns
            .Select(_registryService.FindInstalledProduct)
            .FirstOrDefault(p => p is not null);

        return new ComponentInfo
        {
            Type = ComponentType.KontinentAp,
            Name = "Континент-АП (опционально)",
            InstalledVersion = product?.DisplayVersion,
            Description = product is null
                ? "Не установлен — проверка не требуется"
                : "Обнаружен: приложение не меняет его настройки, интеграцию с КриптоПро выполняйте по документации продукта",
            Status = product is null ? StatusLevel.NotChecked : StatusLevel.Ok
        };
    }

    private ComponentInfo BuildComponentInfo(
        ComponentType type, string name, string? installedVersion, string minVersion, string recommendedVersion, string description)
    {
        var status = _versionService.EvaluateStatus(installedVersion, minVersion, recommendedVersion);

        return new ComponentInfo
        {
            Type = type,
            Name = name,
            InstalledVersion = installedVersion,
            MinimumVersion = minVersion,
            RecommendedVersion = recommendedVersion,
            Description = description,
            Status = status,
            ErrorMessage = status == StatusLevel.Error
                ? (installedVersion is null ? $"{name} не установлен" : $"Версия {installedVersion} ниже минимально допустимой {minVersion}")
                : null
        };
    }
}
