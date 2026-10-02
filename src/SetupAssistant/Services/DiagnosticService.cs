using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IDiagnosticService"/>
public sealed class DiagnosticService : IDiagnosticService
{
    private readonly ICryptoProService _cryptoProService;
    private readonly IBrowserService _browserService;
    private readonly IExtensionService _extensionService;
    private readonly ITokenService _tokenService;
    private readonly ICertificateService _certificateService;
    private readonly INativeMessagingService _nativeMessagingService;
    private readonly ICaCertificateService _caCertificateService;
    private readonly ILoggingService _logger;

    public DiagnosticService(
        ICryptoProService cryptoProService,
        IBrowserService browserService,
        IExtensionService extensionService,
        ITokenService tokenService,
        ICertificateService certificateService,
        INativeMessagingService nativeMessagingService,
        ICaCertificateService caCertificateService,
        ILoggingService logger)
    {
        _nativeMessagingService = nativeMessagingService;
        _caCertificateService = caCertificateService;
        _cryptoProService = cryptoProService;
        _browserService = browserService;
        _extensionService = extensionService;
        _tokenService = tokenService;
        _certificateService = certificateService;
        _logger = logger;
    }

    public Task<IReadOnlyList<ComponentInfo>> RunFullDiagnosticAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<ComponentInfo>>(() =>
        {
            _logger.Info("Диагностика", "Запущена полная диагностика рабочего места");
            var results = new List<ComponentInfo>();

            void Check(string label, Func<ComponentInfo> check)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(label);
                var info = check();
                results.Add(info);
                _logger.Info($"Диагностика: {info.Name}", info.Status.ToString());
            }

            Check("Проверка КриптоПро CSP...", _cryptoProService.CheckCsp);
            Check("Проверка провайдеров КриптоПро...", _cryptoProService.CheckProviders);
            Check("Проверка считывателей КриптоПро...", _cryptoProService.CheckReaders);
            Check("Проверка ключевых контейнеров...", _cryptoProService.CheckContainers);
            Check("Проверка КриптоПро Browser Plugin...", _cryptoProService.CheckBrowserPlugin);
            Check("Проверка COM-компонента CAdESCOM...", _nativeMessagingService.CheckCadesCom);

            foreach (var browser in _browserService.CheckAllBrowsers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Проверка браузера {browser.Name}...");
                results.Add(browser);
            }

            Check("Проверка Контур.Плагина...", _browserService.CheckKonturPlugin);
            Check("Проверка Плагина Госуслуг...", _browserService.CheckGosuslugiPlugin);

            foreach (var extension in _extensionService.CheckAllExtensions())
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Проверка расширения {extension.Name}...");
                results.Add(extension);
                _logger.Info($"Диагностика: {extension.Name}", extension.Status.ToString());
            }

            foreach (var host in _nativeMessagingService.CheckHosts())
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Проверка {host.Name}...");
                results.Add(host);
                _logger.Info($"Диагностика: {host.Name}", host.Status.ToString());
            }

            if (_caCertificateService.ConfiguredCount > 0)
            {
                Check("Проверка сертификатов УЦ...", _caCertificateService.Check);
            }

            Check("Проверка драйвера Rutoken...", _tokenService.CheckRutokenDriver);
            Check("Проверка драйвера JaCarta...", _tokenService.CheckJaCartaDriver);
            Check("Проверка Континент-АП...", _cryptoProService.CheckKontinentAp);
            Check("Проверка сертификатов...", _certificateService.CheckCertificatesStatus);

            progress?.Report("Проверка подключенных носителей...");
            var connectedTokens = _tokenService.GetConnectedTokens();
            results.Add(new ComponentInfo
            {
                Type = ComponentType.Token,
                Name = "Подключенный носитель",
                Description = "Аппаратный носитель электронной подписи",
                Status = connectedTokens.Count > 0 ? StatusLevel.Ok : StatusLevel.Warning,
                InstalledVersion = connectedTokens.Count > 0 ? connectedTokens[0].DeviceName : null,
                ErrorMessage = connectedTokens.Count > 0 ? null : "Носитель не подключен"
            });

            _logger.Info("Диагностика", $"Диагностика завершена. Проверено компонентов: {results.Count}");
            return results;
        }, cancellationToken);
    }
}
