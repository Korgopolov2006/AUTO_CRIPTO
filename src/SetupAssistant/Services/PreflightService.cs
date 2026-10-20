using System.IO;
using System.Net.Http;
using SetupAssistant.Helpers;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IPreflightService"/>
public sealed class PreflightService : IPreflightService
{
    private const long MinFreeBytesError = 1L * 1024 * 1024 * 1024;
    private const long MinFreeBytesWarning = 3L * 1024 * 1024 * 1024;

    private readonly ISystemService _systemService;
    private readonly IConfigService _configService;
    private readonly ILoggingService _logger;

    public PreflightService(ISystemService systemService, IConfigService configService, ILoggingService logger)
    {
        _systemService = systemService;
        _configService = configService;
        _logger = logger;
    }

    public Task<IReadOnlyList<PreflightCheck>> RunAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<PreflightCheck>>(async () =>
        {
            var checks = new List<PreflightCheck>
            {
                CheckAdministrator(),
                CheckDiskSpace(),
                CheckInstallerFiles(),
                await CheckNetworkAsync(cancellationToken),
                CheckPendingReboot(),
                CheckInstallerBusy()
            };

            foreach (var check in checks)
            {
                _logger.Info($"Предварительная проверка: {check.Name}", $"{check.Status}: {check.Message}");
            }

            return checks;
        }, cancellationToken);
    }

    private PreflightCheck CheckAdministrator()
    {
        const string name = "Права администратора";
        return _systemService.GetSystemInfo().IsAdministrator
            ? new PreflightCheck(name, StatusLevel.Ok, "Приложение запущено от имени администратора")
            : new PreflightCheck(name, StatusLevel.Error, "Для установки нужны права администратора: запустите приложение от имени администратора");
    }

    private static PreflightCheck CheckDiskSpace()
    {
        const string name = "Место на диске";
        try
        {
            var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var free = new DriveInfo(systemRoot).AvailableFreeSpace;
            var freeText = $"{free / 1024.0 / 1024 / 1024:0.0} ГБ свободно на {systemRoot}";

            if (free < MinFreeBytesError)
            {
                return new PreflightCheck(name, StatusLevel.Error, $"Недостаточно места: {freeText}, для установки нужно не менее 1 ГБ");
            }

            return free < MinFreeBytesWarning
                ? new PreflightCheck(name, StatusLevel.Warning, $"Места мало: {freeText}, рекомендуется не менее 3 ГБ")
                : new PreflightCheck(name, StatusLevel.Ok, freeText);
        }
        catch (Exception ex)
        {
            return new PreflightCheck(name, StatusLevel.Warning, $"Не удалось определить свободное место: {ex.Message}");
        }
    }

    private PreflightCheck CheckInstallerFiles()
    {
        const string name = "Дистрибутивы";
        var installers = _configService.Current.Installers;
        var definitions = new (string Title, InstallerDefinition Installer)[]
        {
            ("КриптоПро CSP", installers.CryptoProCsp),
            ("КриптоПро Browser Plugin", installers.CryptoProBrowserPlugin),
            ("Драйвер Рутокен", installers.RutokenDriver),
            ("Chromium GOST", installers.ChromiumGost),
            ("Контур.Плагин", installers.KonturPlugin),
            ("Плагин Госуслуг", installers.GosuslugiPlugin),
            ("Континент-АП", installers.KontinentAp)
        };

        // Без локального файла и без адреса для скачивания установить компонент нельзя; с адресом нужен интернет (см. проверку сети).
        var missing = definitions
            .Where(d => d.Installer.IsConfigured
                        && !LocalFileExists(d.Installer.Path)
                        && string.IsNullOrWhiteSpace(d.Installer.DownloadUrl))
            .Select(d => d.Title)
            .ToList();

        return missing.Count == 0
            ? new PreflightCheck(name, StatusLevel.Ok, "Все нужные дистрибутивы на месте")
            : new PreflightCheck(name, StatusLevel.Warning, $"Не найдены файлы: {string.Join(", ", missing)}. Эти шаги завершатся ошибкой");
    }

    private bool LocalFileExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return File.Exists(_configService.ResolvePath(path));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<PreflightCheck> CheckNetworkAsync(CancellationToken cancellationToken)
    {
        const string name = "Сеть и интернет";
        var downloadable = new[] { _configService.Current.Installers.ChromiumGost, _configService.Current.Installers.KonturPlugin }
            .Where(i => Uri.TryCreate(i.DownloadUrl, UriKind.Absolute, out _))
            .ToList();

        if (downloadable.Count == 0)
        {
            return new PreflightCheck(name, StatusLevel.Ok, "Интернет не нужен: все дистрибутивы локальные");
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            var host = new Uri(downloadable[0].DownloadUrl);
            using var response = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, host.GetLeftPart(UriPartial.Authority)), cancellationToken);
            return new PreflightCheck(name, StatusLevel.Ok, "Доступ к серверам загрузки есть: последние версии скачаются автоматически");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PreflightCheck(name, StatusLevel.Warning,
                "Нет доступа в интернет (или он закрыт прокси): Chromium GOST и Контур.Плагин будут установлены из локальных файлов, если они есть");
        }
    }

    private static PreflightCheck CheckPendingReboot()
    {
        const string name = "Перезагрузка Windows";

        // Посторонние отложенные операции с файлами (у многих компьютеров они есть всегда) не считаются: см. WindowsInstallerState.
        return WindowsInstallerState.GetPendingRebootReason() is { } reason
            ? new PreflightCheck(name, StatusLevel.Warning, $"Рекомендуем перезагрузить компьютер до установки: {reason}")
            : new PreflightCheck(name, StatusLevel.Ok, "Ожидающей перезагрузки нет");
    }

    private static PreflightCheck CheckInstallerBusy()
    {
        const string name = "Установщик Windows";
        return WindowsInstallerState.IsBusy()
            ? new PreflightCheck(name, StatusLevel.Warning, "Сейчас выполняется другая установка (возможно, незавершённая после отмены). Помощник дождётся её окончания перед установкой каждого компонента")
            : new PreflightCheck(name, StatusLevel.Ok, "Другие установки не выполняются");
    }
}
