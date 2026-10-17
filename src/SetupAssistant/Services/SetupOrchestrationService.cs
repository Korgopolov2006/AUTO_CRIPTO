using System.IO;
using SetupAssistant.Helpers;
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
    private readonly IWorkplaceStatusService _statusService;
    private readonly ITokenService _tokenService;
    private readonly ICertificateService _certificateService;
    private readonly ICaCertificateService _caCertificateService;
    private readonly IDialogService _dialogService;
    private readonly ITokenPinService _tokenPinService;
    private readonly ILoggingService _logger;
    private readonly IVersionService _versionService;
    private SetupRunOptions _options = new();

    /// <summary>Установки, фактически выполненные в текущем запуске: если проверка после них не прошла, вероятно, нужна перезагрузка.</summary>
    private readonly HashSet<SetupStepKind> _installedInThisRun = new();

    public SetupOrchestrationService(
        IConfigService configService,
        IInstallerService installerService,
        IDownloadService downloadService,
        ICryptoProService cryptoProService,
        IBrowserService browserService,
        IExtensionService extensionService,
        IWorkplaceStatusService statusService,
        ITokenService tokenService,
        ICertificateService certificateService,
        ICaCertificateService caCertificateService,
        IDialogService dialogService,
        ITokenPinService tokenPinService,
        ILoggingService logger,
        IVersionService versionService)
    {
        _versionService = versionService;
        _dialogService = dialogService;
        _tokenPinService = tokenPinService;
        _tokenService = tokenService;
        _certificateService = certificateService;
        _caCertificateService = caCertificateService;
        _configService = configService;
        _installerService = installerService;
        _downloadService = downloadService;
        _cryptoProService = cryptoProService;
        _browserService = browserService;
        _extensionService = extensionService;
        _statusService = statusService;
        _logger = logger;
    }

    public async Task RunSetupAsync(IReadOnlyList<SetupStepModel> steps, IProgress<SetupStepModel>? progress, SetupRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        var installers = _configService.Current.Installers;
        _options = options ?? new SetupRunOptions();
        _installedInThisRun.Clear();

        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!step.IsSelected)
            {
                step.Status = StatusLevel.Skipped;
                step.Details = "Не выбрано — шаг пропущен";
                progress?.Report(step);
                continue;
            }

            step.NeedsReboot = false;
            step.IsActive = true;
            step.Status = StatusLevel.InProgress;
            progress?.Report(step);

            try
            {
                await ExecuteStepAsync(step, installers, cancellationToken);
                MarkRebootIfLikely(step);
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
                step.Details = extResult.Success ? "Политика установки расширений записана" : extResult.ErrorMessage;

                if (extResult.Success)
                {
                    var management = _extensionService.CheckDeviceManagement();
                    if (management.Status != StatusLevel.Ok)
                    {
                        step.Status = StatusLevel.Warning;
                        step.Details = $"Политика записана, но может быть не применена. {management.ErrorMessage}";
                        _logger.Warning($"Настройка: {step.Title}", "Компьютер не управляется (домен/MDM)", management.ErrorMessage);
                    }
                }
                break;

            case SetupStepKind.InstallTokenCertificates:
                await InstallTokenCertificatesAsync(step, cancellationToken);
                break;

            case SetupStepKind.FinalDiagnostic:
                var summary = await _statusService.RefreshAsync(cancellationToken);
                step.Status = summary.Problems.Count > 0 ? StatusLevel.Warning : StatusLevel.Ok;
                step.Details = summary.Problems.Count == 0
                    ? $"Проверено компонентов: {summary.Results.Count}, проблем не найдено"
                    : $"Проверено компонентов: {summary.Results.Count}, требуют внимания: {summary.Problems.Count}";
                break;
        }
    }

    /// <summary>
    /// Проверка не прошла сразу после успешной установки (или Windows ждёт перезагрузки): компоненты часто становятся видны
    /// системе только после неё. Шаг остаётся с ошибкой, но сводка предложит перезагрузку и продолжение настройки.
    /// </summary>
    private void MarkRebootIfLikely(SetupStepModel step)
    {
        if (step.PairedInstallKind is not { } installKind || step.Status is StatusLevel.Ok or StatusLevel.Skipped or StatusLevel.NotChecked)
        {
            return;
        }

        if (!_installedInThisRun.Contains(installKind) && !WindowsInstallerState.IsRebootPending())
        {
            return;
        }

        step.NeedsReboot = true;
        step.Details = $"{step.Details} Установка выполнена, но компонент ещё не виден системе — обычно это решается перезагрузкой компьютера.".Trim();
        _logger.Warning($"Настройка: {step.Title}", "Проверка не прошла после установки: вероятно, нужна перезагрузка компьютера");
    }

    /// <summary>Проверка уже установленного компонента для шага установки; null — для шага проверки нет.</summary>
    private ComponentInfo? CheckInstalled(SetupStepKind kind) => kind switch
    {
        SetupStepKind.InstallCryptoProCsp => _cryptoProService.CheckCsp(),
        SetupStepKind.InstallBrowserPlugin => _cryptoProService.CheckBrowserPlugin(),
        SetupStepKind.InstallRutokenDriver => _tokenService.CheckRutokenDriver(),
        SetupStepKind.InstallKonturPlugin => _browserService.CheckKonturPlugin(),
        SetupStepKind.InstallGosuslugiPlugin => _browserService.CheckGosuslugiPlugin(),
        SetupStepKind.InstallChromiumGost => _configService.Current.SupportedBrowsers
            .Where(b => b.Kind == BrowserKind.ChromiumGost)
            .Select(_browserService.CheckBrowser)
            .FirstOrDefault(),
        _ => null
    };

    private async Task RunInstall(SetupStepModel step, InstallerDefinition installer, CancellationToken cancellationToken)
    {
        string? installedVersion = null;
        var updating = false;

        if (!_options.ForceReinstall && CheckInstalled(step.Kind) is { Status: StatusLevel.Ok } existing)
        {
            // Chromium GOST выходит часто и не имеет зафиксированной версии: при установке подтягиваем актуальную.
            var newer = step.Kind == SetupStepKind.InstallChromiumGost
                ? await FindNewerChromiumVersionAsync(installer, existing.InstalledVersion, cancellationToken)
                : null;

            if (newer is null)
            {
                step.Status = StatusLevel.Ok;
                step.Details = string.IsNullOrEmpty(existing.InstalledVersion)
                    ? "Уже установлено — установка не требуется"
                    : $"Уже установлено (версия {existing.InstalledVersion}), актуальная — установка не требуется";
                _logger.Info($"Настройка: {step.Title}", "Пропущено: компонент уже установлен");
                return;
            }

            updating = true;
            installedVersion = existing.InstalledVersion;
            step.Details = $"Обновление: {installedVersion} → {newer}";
            _logger.Info($"Настройка: {step.Title}", $"Доступна новая версия: установлена {installedVersion}, будет установлена {newer}");
        }

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
                var signature = Authenticode.Verify(downloaded, installer.ExpectedSigner);
                if (!signature.IsValid)
                {
                    _logger.Error($"Настройка: {step.Title}", "Скачанный файл отклонён", $"{signature.Message}: {Path.GetFileName(downloaded)}");
                    File.Delete(downloaded);
                    downloaded = null;
                }
            }

            if (downloaded is not null)
            {
                installer = new InstallerDefinition { Path = downloaded, Kind = installer.Kind, SilentArgs = installer.SilentArgs };
            }
            else if (string.IsNullOrWhiteSpace(installer.Path))
            {
                step.Status = StatusLevel.Error;
                step.Details = "Не удалось скачать дистрибутив (нет связи или не пройдена проверка подписи), проверьте журнал";
                return;
            }
            else if (updating && _versionService.Compare(ExtractVersion(installer.Path), installedVersion) <= 0)
            {
                // Встроенный дистрибутив не новее установленного версии: ставить его значило бы понизить версию.
                step.Status = StatusLevel.Warning;
                step.Details = $"Установлена версия {installedVersion}; новую версию скачать не удалось, а встроенная не новее. Повторите при наличии интернета";
                _logger.Warning($"Настройка: {step.Title}", "Обновление пропущено: новую версию скачать не удалось, встроенная не новее установленной");
                return;
            }
            else
            {
                _logger.Warning($"Настройка: {step.Title}", "Скачать последнюю версию не удалось, используется локальный дистрибутив");
            }
        }

        var status = new Progress<string>(message => step.Details = message);
        step.Details = "Установка запущена…";
        var result = await _installerService.InstallAsync(installer, status, cancellationToken);

        step.Status = result.Success ? StatusLevel.Ok : StatusLevel.Error;
        step.NeedsReboot = result.Success && result.RebootRequired;
        if (result.Success)
        {
            _installedInThisRun.Add(step.Kind);
        }

        step.Details = result.Success
            ? result.RebootRequired
                ? "Установка завершена, требуется перезагрузка компьютера"
                : "Установка завершена успешно"
            : result.ErrorMessage;
    }

    /// <summary>
    /// Версия Chromium GOST новее установленной: берётся большая из последней на GitHub и встроенной в приложение
    /// (версия читается из имени файла). null — обновление не нужно или не из чего выбрать.
    /// </summary>
    private async Task<string?> FindNewerChromiumVersionAsync(InstallerDefinition installer, string? installedVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            return null;
        }

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(installer.Path) && ExtractVersion(installer.Path) is { } local)
        {
            candidates.Add(local);
        }

        if (!string.IsNullOrWhiteSpace(installer.DownloadUrl) && !string.IsNullOrWhiteSpace(installer.DownloadAssetRegex))
        {
            var assetName = await _downloadService.GetLatestAssetNameAsync(installer.DownloadUrl, installer.DownloadAssetRegex, cancellationToken);
            if (assetName is not null && ExtractVersion(assetName) is { } online)
            {
                candidates.Add(online);
            }
        }

        var latest = candidates.OrderByDescending(v => v, Comparer<string>.Create(_versionService.Compare)).FirstOrDefault();
        return latest is not null && _versionService.Compare(latest, installedVersion) > 0 ? latest : null;
    }

    private static string? ExtractVersion(string fileNameOrPath)
    {
        var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(fileNameOrPath), @"\d+(\.\d+){2,3}");
        return match.Success ? match.Value : null;
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
        var (token, skipped) = await WaitForTokenAsync(step, cancellationToken);
        if (token is null)
        {
            step.Status = StatusLevel.Warning;
            step.Details = skipped
                ? "Ожидание носителя пропущено. Подключите его и повторите настройку либо установите сертификат вручную через КриптоПро CSP."
                : "Носитель не подключён. Подключите его и повторите настройку либо установите сертификат вручную через КриптоПро CSP.";
            return;
        }

        step.Details = $"Носитель обнаружен: {token.DeviceName}. Установка сертификатов, при запросе введите PIN-код…";

        // Система и КриптоПро видят подключённый токен не мгновенно.
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        // КриптоПро может запросить PIN-код в своём окне (оно открывается отдельно и легко теряется за окном приложения).
        // Окно-подсказка появляется, только если импорт не завершился сразу, и закрывается само.
        using var pinPrompt = _dialogService.ShowActionPrompt(
            "Ожидается ввод PIN-кода",
            "КриптоПро читает сертификаты с носителя. Если появится окно запроса PIN-кода, введите его — окно может открыться " +
            "за окном приложения или в панели задач.\n\nЭто сообщение закроется автоматически, когда установка завершится.",
            "Пропустить",
            showAfter: TimeSpan.FromSeconds(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, pinPrompt.Cancelled);

        try
        {
            var result = await _certificateService.ImportFromContainersAsync(linked.Token);
            step.Status = result.Status;
            step.Details = result.Message;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warning($"Настройка: {step.Title}", "Установка сертификатов с носителя пропущена пользователем");
            step.Status = StatusLevel.Warning;
            step.Details = "Установка сертификатов пропущена. Повторите настройку или установите сертификат вручную через КриптоПро CSP.";
        }
    }

    /// <summary>
    /// Если носитель уже подключён — возвращает его сразу; иначе показывает модальное окно с просьбой подключить носитель
    /// и ждёт его заданное время (по умолчанию 60 с) с обратным отсчётом. Из окна можно выбрать «Пропустить».
    /// </summary>
    private async Task<(TokenInfo? Token, bool Skipped)> WaitForTokenAsync(SetupStepModel step, CancellationToken cancellationToken)
    {
        var connected = await FindTokenAsync(cancellationToken);
        if (connected is not null)
        {
            return (connected, false);
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(5, _configService.Current.CheckSettings.TokenWaitTimeoutSeconds));
        var deadline = DateTime.Now + timeout;

        static string Message(int secondsLeft) =>
            "Подключите носитель электронной подписи (Рутокен или JaCarta) к USB-порту компьютера.\n\n" +
            $"Окно закроется само, как только носитель будет обнаружен. Осталось ждать: {secondsLeft} с.\n" +
            "Если носитель сейчас не нужен, нажмите «Пропустить».";

        using var prompt = _dialogService.ShowActionPrompt("Подключите носитель", Message((int)timeout.TotalSeconds), "Пропустить");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, prompt.Cancelled);
        _logger.Info("Ожидание носителя", $"Запрошено подключение носителя, ожидание {(int)timeout.TotalSeconds} с");

        try
        {
            while (DateTime.Now < deadline)
            {
                linked.Token.ThrowIfCancellationRequested();

                var secondsLeft = (int)Math.Ceiling((deadline - DateTime.Now).TotalSeconds);
                prompt.UpdateMessage(Message(secondsLeft));
                step.Details = $"Ожидание носителя: осталось {secondsLeft} с";

                var token = await FindTokenAsync(linked.Token);
                if (token is not null)
                {
                    _logger.Info("Ожидание носителя", $"Обнаружен носитель: {token.DeviceName}");
                    return (token, false);
                }

                await Task.Delay(1000, linked.Token);
            }

            _logger.Warning("Ожидание носителя", "Носитель не был подключён за отведённое время");
            return (null, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Info("Ожидание носителя", "Ожидание пропущено пользователем");
            return (null, true);
        }
    }

    /// <summary>Ищет подключённый носитель двумя способами: по устройствам Windows и через драйвер производителя (PKCS#11).</summary>
    private async Task<TokenInfo?> FindTokenAsync(CancellationToken cancellationToken)
    {
        var byDevice = await Task.Run(() => _tokenService.GetConnectedTokens().FirstOrDefault(), cancellationToken);
        if (byDevice is not null)
        {
            return byDevice;
        }

        try
        {
            var scan = await _tokenPinService.ScanAsync(cancellationToken);
            return scan.Tokens.Select(t => new TokenInfo
            {
                Type = t.Provider == "JaCarta" ? TokenType.JaCarta : TokenType.Rutoken,
                DeviceName = string.IsNullOrWhiteSpace(t.Label) ? t.Model : $"{t.Label} ({t.Model})",
                SerialNumber = t.SerialNumber,
                IsConnected = true,
                DriverInstalled = true
            }).FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Драйвер производителя недоступен — полагаемся на определение по устройствам Windows.
            return null;
        }
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
