using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="ISetupOrchestrationService"/>
public sealed class SetupOrchestrationService : ISetupOrchestrationService
{
    private readonly IConfigService _configService;
    private readonly IInstallerService _installerService;
    private readonly IDownloadService _downloadService;
    private readonly ICryptoProService _cryptoProService;
    private readonly IBrowserService _browserService;
    private readonly IExtensionService _extensionService;
    private readonly IDiagnosticService _diagnosticService;
    private readonly ITokenService _tokenService;
    private readonly ICertificateService _certificateService;
    private readonly ICaCertificateService _caCertificateService;
    private readonly ILoggingService _logger;

    public SetupOrchestrationService(
        IConfigService configService,
        IInstallerService installerService,
        IDownloadService downloadService,
        ICryptoProService cryptoProService,
        IBrowserService browserService,
        IExtensionService extensionService,
        IDiagnosticService diagnosticService,
        ITokenService tokenService,
        ICertificateService certificateService,
        ICaCertificateService caCertificateService,
        ILoggingService logger)
    {
        _tokenService = tokenService;
        _certificateService = certificateService;
        _caCertificateService = caCertificateService;
        _configService = configService;
        _installerService = installerService;
        _downloadService = downloadService;
        _cryptoProService = cryptoProService;
        _browserService = browserService;
        _extensionService = extensionService;
        _diagnosticService = diagnosticService;
        _logger = logger;
    }

    public async Task RunSetupAsync(IReadOnlyList<SetupStepModel> steps, IProgress<SetupStepModel>? progress, CancellationToken cancellationToken = default)
    {
        var installers = _configService.Current.Installers;

        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            step.IsActive = true;
            step.Status = StatusLevel.InProgress;
            progress?.Report(step);

            try
            {
                await ExecuteStepAsync(step, installers, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                step.Status = StatusLevel.Error;
                step.Details = "Настройка отменена пользователем";
                progress?.Report(step);
                throw;
            }
            catch (Exception ex)
            {
                step.Status = StatusLevel.Error;
                step.Details = ex.Message;
                _logger.Error($"Настройка: {step.Title}", "Ошибка выполнения шага", ex.Message);
            }
            finally
            {
                step.IsActive = false;
                progress?.Report(step);
            }
        }
    }

    private async Task ExecuteStepAsync(SetupStepModel step, InstallersConfig installers, CancellationToken cancellationToken)
    {
        switch (step.Kind)
        {
            case SetupStepKind.InstallCryptoProCsp:
                await RunInstall(step, installers.CryptoProCsp, cancellationToken);
                break;

            case SetupStepKind.VerifyCryptoProCsp:
                VerifyComponent(step, _cryptoProService.CheckCsp());
                break;

            case SetupStepKind.InstallBrowserPlugin:
                await RunInstall(step, installers.CryptoProBrowserPlugin, cancellationToken);
                break;

            case SetupStepKind.VerifyBrowserPlugin:
                VerifyComponent(step, _cryptoProService.CheckBrowserPlugin());
                break;

            case SetupStepKind.InstallRutokenDriver:
                await RunInstall(step, installers.RutokenDriver, cancellationToken);
                break;

            case SetupStepKind.VerifyRutokenDriver:
                VerifyComponent(step, _tokenService.CheckRutokenDriver());
                break;

            case SetupStepKind.InstallGosuslugiPlugin:
                await RunInstall(step, installers.GosuslugiPlugin, cancellationToken);
                break;

            case SetupStepKind.VerifyGosuslugiPlugin:
                VerifyComponent(step, _browserService.CheckGosuslugiPlugin());
                break;

            case SetupStepKind.InstallCaCertificates:
                await InstallCaCertificatesAsync(step, cancellationToken);
                break;

            case SetupStepKind.InstallChromiumGost:
                await RunInstall(step, installers.ChromiumGost, cancellationToken);
                break;

            case SetupStepKind.VerifyChromiumGost:
                var chromiumGost = _configService.Current.SupportedBrowsers.FirstOrDefault(b => b.Kind == BrowserKind.ChromiumGost);
                VerifyComponent(step, chromiumGost is null ? null : _browserService.CheckBrowser(chromiumGost));
                break;

            case SetupStepKind.InstallKonturPlugin:
                await RunInstall(step, installers.KonturPlugin, cancellationToken);
                break;

            case SetupStepKind.VerifyKonturPlugin:
                VerifyComponent(step, _browserService.CheckKonturPlugin());
                break;

            case SetupStepKind.InstallBrowserExtensions:
                var extResult = await _extensionService.InstallExtensionsAsync(cancellationToken);
                step.Status = extResult.Success ? StatusLevel.Ok : StatusLevel.Error;
                step.Details = extResult.Success ? "Расширения установлены" : extResult.ErrorMessage;
                break;

            case SetupStepKind.InstallTokenCertificates:
                await InstallTokenCertificatesAsync(step, cancellationToken);
                break;

            case SetupStepKind.FinalDiagnostic:
                var results = await _diagnosticService.RunFullDiagnosticAsync(cancellationToken: cancellationToken);
                var hasErrors = results.Any(r => r.Status == StatusLevel.Error);
                step.Status = hasErrors ? StatusLevel.Warning : StatusLevel.Ok;
                step.Details = $"Проверено компонентов: {results.Count}, с ошибками: {results.Count(r => r.Status == StatusLevel.Error)}";
                break;
        }
    }

    private async Task RunInstall(SetupStepModel step, InstallerDefinition installer, CancellationToken cancellationToken)
    {
        if (!installer.IsConfigured)
        {
            step.Status = StatusLevel.Warning;
            step.Details = "Дистрибутив не задан в Config.json — шаг пропущен";
            _logger.Warning($"Настройка: {step.Title}", "Шаг пропущен: путь к дистрибутиву не задан");
            return;
        }

        if (!string.IsNullOrWhiteSpace(installer.DownloadUrl))
        {
            step.Details = "Загрузка последней версии...";
            var downloaded = await _downloadService.DownloadAsync(installer.DownloadUrl, installer.DownloadAssetRegex, null, cancellationToken);

            if (downloaded is not null)
            {
                installer = new InstallerDefinition { Path = downloaded, Kind = installer.Kind, SilentArgs = installer.SilentArgs };
            }
            else if (string.IsNullOrWhiteSpace(installer.Path))
            {
                step.Status = StatusLevel.Error;
                step.Details = "Не удалось скачать дистрибутив, проверьте подключение к интернету";
                return;
            }
            else
            {
                _logger.Warning($"Настройка: {step.Title}", "Скачать последнюю версию не удалось, используется локальный дистрибутив");
            }
        }

        var result = await _installerService.InstallAsync(installer, cancellationToken);

        step.Status = result.Success ? StatusLevel.Ok : StatusLevel.Error;
        step.Details = result.Success
            ? result.RebootRequired
                ? "Установка завершена, требуется перезагрузка компьютера"
                : "Установка завершена успешно"
            : result.ErrorMessage;
    }

    private async Task InstallCaCertificatesAsync(SetupStepModel step, CancellationToken cancellationToken)
    {
        if (_caCertificateService.ConfiguredCount == 0)
        {
            step.Status = StatusLevel.Warning;
            step.Details = "Сертификаты УЦ не заданы в Config.json (раздел CaCertificates) — шаг пропущен";
            _logger.Warning($"Настройка: {step.Title}", "Шаг пропущен: список сертификатов УЦ пуст");
            return;
        }

        var result = await _caCertificateService.InstallAsync(cancellationToken);
        step.Status = result.Success ? StatusLevel.Ok : StatusLevel.Error;
        step.Details = result.Success ? $"Сертификаты УЦ установлены: {_caCertificateService.ConfiguredCount}" : result.ErrorMessage;
    }

    /// <summary>Ждёт подключения носителя, затем устанавливает сертификаты из его контейнеров в хранилище «Личное».</summary>
    private async Task InstallTokenCertificatesAsync(SetupStepModel step, CancellationToken cancellationToken)
    {
        var token = await _tokenService.WaitForTokenAsync(new Progress<string>(text => step.Details = text), cancellationToken);
        if (token is null)
        {
            step.Status = StatusLevel.Warning;
            step.Details = "Носитель не подключён. Подключите его и повторите настройку либо установите сертификат вручную через КриптоПро CSP.";
            return;
        }

        step.Details = $"Носитель обнаружен: {token.DeviceName}. Установка сертификатов, при запросе введите PIN-код…";

        // Система и КриптоПро видят подключённый токен не мгновенно.
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        var result = await _certificateService.ImportFromContainersAsync(cancellationToken);
        step.Status = result.Status;
        step.Details = result.Message;
    }

    private static void VerifyComponent(SetupStepModel step, ComponentInfo? component)
    {
        if (component is null)
        {
            step.Status = StatusLevel.Error;
            step.Details = "Компонент не найден в конфигурации";
            return;
        }

        step.Status = component.Status;
        step.Details = component.Status == StatusLevel.Ok
            ? $"Версия {component.InstalledVersion} — проверка пройдена"
            : component.ErrorMessage;
    }
}
