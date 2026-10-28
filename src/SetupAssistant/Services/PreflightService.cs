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

    // Удалению нужно место под откатную информацию Windows Installer и копии при полной очистке, но не гигабайты.
    private const long MinFreeBytesUninstallError = 100L * 1024 * 1024;
    private const long MinFreeBytesUninstallWarning = 500L * 1024 * 1024;

    private readonly ISystemService _systemService;
    private readonly IConfigService _configService;
    private readonly ILoggingService _logger;
    private readonly IUninstallService _uninstallService;

    public PreflightService(ISystemService systemService, IConfigService configService, ILoggingService logger, IUninstallService uninstallService)
    {
        _systemService = systemService;
        _configService = configService;
        _logger = logger;
        _uninstallService = uninstallService;
    }

    public Task<IReadOnlyList<PreflightCheck>> RunAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<PreflightCheck>>(async () =>
        {
            var checks = new List<PreflightCheck>
            {
                CheckAdministrator("установки"),
                CheckDiskSpace(MinFreeBytesError, MinFreeBytesWarning, "установки"),
                CheckInstallerFiles(),
                await CheckNetworkAsync(cancellationToken),
                CheckPendingReboot("установки"),
                CheckInstallerBusy("установкой каждого компонента"),
                CheckLockedFiles(uninstall: false)
            };

            LogChecks(checks);
            return checks;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<PreflightCheck>> RunForUninstallAsync(bool fullCleanup, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<PreflightCheck>>(() =>
        {
            var checks = new List<PreflightCheck>
            {
                CheckAdministrator("удаления"),
                CheckDiskSpace(MinFreeBytesUninstallError, MinFreeBytesUninstallWarning, "удаления"),
                CheckPendingReboot("удаления"),
                CheckInstallerBusy("удалением каждого компонента"),
                CheckLockedFiles(uninstall: true),
                CheckSomethingToRemove()
            };

            if (fullCleanup)
            {
                checks.Add(CheckReportDirectoryWritable());
            }

            LogChecks(checks);
            return checks;
        }, cancellationToken);
    }

    private void LogChecks(IEnumerable<PreflightCheck> checks)
    {
        foreach (var check in checks)
        {
            _logger.Info($"Предварительная проверка: {check.Name}", $"{check.Status}: {check.Message}");
        }
    }

    private PreflightCheck CheckAdministrator(string action)
    {
        const string name = "Права администратора";
        return _systemService.GetSystemInfo().IsAdministrator
            ? new PreflightCheck(name, StatusLevel.Ok, "Приложение запущено от имени администратора")
            : new PreflightCheck(name, StatusLevel.Error, $"Для {action} нужны права администратора: запустите приложение от имени администратора",
                "Закройте приложение, нажмите на SetupAssistant.exe правой кнопкой и выберите «Запуск от имени администратора». В режиме командной строки откройте консоль от имени администратора.");
    }

    /// <summary>Какие программы держат файлы наших компонентов (браузер с плагином, КриптоПро, службы). Только предупреждение.</summary>
    private static PreflightCheck CheckLockedFiles(bool uninstall)
    {
        const string name = "Занятые файлы";
        IReadOnlyList<LockingApp> apps;
        try
        {
            apps = FileLockInspector.FindForManagedComponents();
        }
        catch (Exception)
        {
            apps = Array.Empty<LockingApp>();
        }

        if (apps.Count == 0)
        {
            return new PreflightCheck(name, StatusLevel.Ok, "Программы не держат файлы КриптоПро, Рутокен и плагинов");
        }

        var closable = apps.Where(a => a.Closable).Select(a => a.ProcessId).ToList();
        var message = "Файлы компонентов используют: " + string.Join(", ", apps.Select(a => a.Display).Take(8))
                      + (apps.Count > 8 ? $" и ещё {apps.Count - 8}" : string.Empty);
        var advice = closable.Count > 0
            ? "Сохраните работу и закройте эти программы — кнопка «Закрыть мешающие программы» делает это аккуратно, с вопросом о несохранённом. "
            : string.Empty;
        advice += uninstall
            ? "Если оставить их открытыми, при удалении приложение закроет их принудительно, а несохранённые данные пропадут."
            : "Если оставить их открытыми, установщик может не заменить файлы и потребовать перезагрузку. Службы установщик остановит сам.";

        return new PreflightCheck(name, StatusLevel.Warning, message, advice, closable);
    }

    private PreflightCheck CheckSomethingToRemove()
    {
        const string name = "Что удалять";
        try
        {
            var installed = _uninstallService.CountInstalledProducts();
            return installed > 0
                ? new PreflightCheck(name, StatusLevel.Ok, $"Найдено установленных компонентов: {installed}")
                : new PreflightCheck(name, StatusLevel.Warning, "Установленных компонентов не найдено: будут убраны только остатки и данные приложения",
                    "Если вы ожидали увидеть программы, проверьте «Параметры → Приложения → Установленные приложения» и запустите приложение от имени администратора. " +
                    "Если удалить нужно только следы прошлых установок, можно продолжать.");
        }
        catch (Exception ex)
        {
            return new PreflightCheck(name, StatusLevel.Warning, $"Не удалось определить, что установлено: {ex.Message}");
        }
    }

    /// <summary>Резервные копии и отчёт полной очистки пишутся рядом с журналами: если туда писать нельзя, очистка потеряет копию ключей.</summary>
    private PreflightCheck CheckReportDirectoryWritable()
    {
        const string name = "Папка отчёта";
        var directory = Path.GetDirectoryName(_logger.CurrentLogFilePath);
        if (string.IsNullOrEmpty(directory))
        {
            return new PreflightCheck(name, StatusLevel.Warning, "Не удалось определить папку журналов: резервная копия контейнеров может не сохраниться",
                "Перезапустите приложение. Если сообщение повторяется, укажите существующую папку в разделе Logging файла Config.json.");
        }

        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return new PreflightCheck(name, StatusLevel.Ok, $"Запись в {directory} возможна");
        }
        catch (Exception ex)
        {
            return new PreflightCheck(name, StatusLevel.Error,
                $"В папку {directory} нельзя записать ({ex.Message}): резервная копия ключевых контейнеров и отчёт не сохранятся",
                "Освободите место на диске с этой папкой и проверьте права на запись (свойства папки → «Безопасность»). " +
                "Или перенесите приложение в папку, куда можно писать, либо укажите другую папку в разделе Logging файла Config.json.");
        }
    }

    private static PreflightCheck CheckDiskSpace(long errorBytes, long warningBytes, string action)
    {
        const string name = "Место на диске";
        try
        {
            var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            var free = new DriveInfo(systemRoot).AvailableFreeSpace;
            var freeText = free >= 1024L * 1024 * 1024
                ? $"{free / 1024.0 / 1024 / 1024:0.0} ГБ свободно на {systemRoot}"
                : $"{free / 1024 / 1024} МБ свободно на {systemRoot}";

            if (free < errorBytes)
            {
                return new PreflightCheck(name, StatusLevel.Error, $"Недостаточно места: {freeText}, для {action} нужно не менее {FormatSize(errorBytes)}", DiskAdvice);
            }

            return free < warningBytes
                ? new PreflightCheck(name, StatusLevel.Warning, $"Места мало: {freeText}, рекомендуется не менее {FormatSize(warningBytes)}", DiskAdvice)
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
            : new PreflightCheck(name, StatusLevel.Warning, $"Не найдены файлы: {string.Join(", ", missing)}. Эти шаги завершатся ошибкой",
                "Положите дистрибутивы в папку Appendices рядом с приложением (пути указаны в Config.json) или подключите интернет, если для них задан адрес загрузки. " +
                "Либо снимите отметку с этих шагов в мастере.");
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
                "Нет доступа в интернет (или он закрыт прокси): Chromium GOST и Контур.Плагин будут установлены из локальных файлов, если они есть",
                "Проверьте подключение и настройки прокси. Без интернета положите установщики Chromium GOST и Контур.Плагина в папку Appendices.");
        }
    }

    private const string DiskAdvice =
        "Освободите место на системном диске: «Параметры → Система → Память → Временные файлы» или «Очистка диска» (cleanmgr), " +
        "удалите ненужные программы и папку «Загрузки», затем нажмите «Проверить снова».";

    private static string FormatSize(long bytes) =>
        bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024 / 1024 / 1024} ГБ" : $"{bytes / 1024 / 1024} МБ";

    private static PreflightCheck CheckPendingReboot(string action)
    {
        const string name = "Перезагрузка Windows";

        // Посторонние отложенные операции с файлами (у многих компьютеров они есть всегда) не считаются: см. WindowsInstallerState.
        return WindowsInstallerState.GetPendingRebootReason() is { } reason
            ? new PreflightCheck(name, StatusLevel.Warning, $"Рекомендуем перезагрузить компьютер до {action}: {reason}",
                "Сохраните работу, перезагрузите компьютер и запустите приложение снова — так надёжнее. " +
                "Можно продолжить и без перезагрузки, но установщик пакета может отказаться работать до неё.")
            : new PreflightCheck(name, StatusLevel.Ok, "Ожидающей перезагрузки нет");
    }

    /// <param name="beforeEach">Фраза для сообщения: «перед … каждого компонента».</param>
    private PreflightCheck CheckInstallerBusy(string beforeEach)
    {
        const string name = "Установщик Windows";
        if (!WindowsInstallerState.IsBusy())
        {
            return new PreflightCheck(name, StatusLevel.Ok, "Другие установки не выполняются");
        }

        var cause = InstallerBusyInspector.Inspect();
        var causeText = cause is null ? string.Empty : $" Сейчас: {cause.Description}.";

        // Операцию КриптоПро, зависшую на драйвере, не дождаться: без перезагрузки каждый следующий установщик встанет за ней.
        return CryptoProDriverHang.FindStuckLog(_configService.ResolvePath(_configService.Current.Logging.LogDirectory)) is { } stuckLog
            ? new PreflightCheck(name, StatusLevel.Error, CryptoProDriverHang.BusyMessage(stuckLog.LastWriteTime) + causeText,
                "Перезагрузите компьютер: зависшую операцию КриптоПро отпускает только перезагрузка, ожидание и завершение процессов не помогут. После входа запустите приложение снова.")
            : new PreflightCheck(name, StatusLevel.Warning,
                $"Сейчас выполняется другая установка (возможно, незавершённая после отмены). Помощник дождётся её окончания перед {beforeEach}.{causeText}",
                InstallerBusyInspector.AdviceFor(cause));
    }
}
