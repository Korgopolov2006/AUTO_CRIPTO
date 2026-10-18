using System.Diagnostics;
using System.IO;
using SetupAssistant.Helpers;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IInstallerService"/>
public sealed class InstallerService : IInstallerService
{
    /// <summary>Код msiexec/установщиков «успешно, требуется перезагрузка».</summary>
    private const int ExitCodeRebootRequired = 3010;

    /// <summary>Код msiexec «другая установка уже выполняется».</summary>
    private const int ExitCodeInstallerBusy = 1618;

    /// <summary>Код msiexec «фатальная ошибка при установке» (в том числе отказ пакета до перезагрузки).</summary>
    private const int ExitCodeFatalInstall = 1603;

    /// <summary>Сколько повторов делать, если Windows Installer занят в момент запуска.</summary>
    private const int MaxBusyAttempts = 3;

    /// <summary>Деинсталляция КриптоПро CSP на медленных компьютерах занимает около получаса — ждём с запасом.</summary>
    private static readonly TimeSpan MaxInstallerWait = TimeSpan.FromMinutes(60);

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HeartbeatLogInterval = TimeSpan.FromMinutes(2);

    private readonly IConfigService _configService;
    private readonly ILoggingService _logger;

    public InstallerService(IConfigService configService, ILoggingService logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public async Task<InstallResult> InstallAsync(InstallerDefinition installer, IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        // В едином exe крупный дистрибутив распаковывается при первом обращении — не блокируем окно.
        var fullPath = await Task.Run(() => _configService.ResolvePath(installer.Path), cancellationToken);

        return installer.Kind == InstallerKind.Msi
            ? await InstallMsiAsync(fullPath, installer.SilentArgs, status, cancellationToken)
            : await InstallExeAsync(fullPath, installer.SilentArgs, status, cancellationToken);
    }

    public async Task<InstallResult> InstallMsiAsync(string msiPath, string additionalArgs = "", IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(msiPath))
        {
            var message = $"Файл установщика не найден: {msiPath}";
            _logger.Error("Установка MSI", "Не выполнена", message);
            return new InstallResult(false, -1, message);
        }

        var args = $"/i \"{msiPath}\" /qn /norestart {additionalArgs}".Trim();
        return await RunProcessAsync("msiexec.exe", args, DefaultActionLabel, status, cancellationToken);
    }

    public async Task<InstallResult> InstallExeAsync(string exePath, string silentArgs = "", IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(exePath))
        {
            var message = $"Файл установщика не найден: {exePath}";
            _logger.Error("Установка EXE", "Не выполнена", message);
            return new InstallResult(false, -1, message);
        }

        return await RunProcessAsync(exePath, silentArgs, DefaultActionLabel, status, cancellationToken);
    }

    public Task<InstallResult> RunAsync(string fileName, string arguments, string actionLabel, IProgress<string>? status = null, CancellationToken cancellationToken = default) =>
        RunProcessAsync(fileName, arguments, actionLabel, status, cancellationToken);

    private const string DefaultActionLabel = "Установка";

    private async Task<InstallResult> RunProcessAsync(string fileName, string arguments, string actionLabel, IProgress<string>? status, CancellationToken cancellationToken)
    {
        var processName = Path.GetFileName(fileName);
        var isMsi = processName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase);
        string? msiLogPath = null;

        try
        {
            for (var attempt = 1; ; attempt++)
            {
                // Windows Installer выполняет одну установку за раз. Если предыдущая (в том числе отменённая в этом окне) ещё идёт,
                // дожидаемся её, а не получаем код 1618.
                if (isMsi)
                {
                    await WaitForWindowsInstallerAsync(actionLabel, processName, status, cancellationToken);
                }

                var runArguments = arguments;
                if (isMsi)
                {
                    (runArguments, msiLogPath) = WithMsiLog(arguments, actionLabel);
                }

                var run = await RunOnceAsync(fileName, runArguments, actionLabel, processName, status, cancellationToken);

                if (isMsi && run.ExitCode == ExitCodeInstallerBusy && attempt < MaxBusyAttempts)
                {
                    _logger.Warning($"{actionLabel} ({processName})", $"Windows Installer занят (код 1618), повтор {attempt + 1} из {MaxBusyAttempts}");
                    continue;
                }

                return LogResult(run, actionLabel, processName, msiLogPath);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Warning($"{actionLabel} ({processName})",
                isMsi
                    ? "Отменена пользователем. Windows Installer может ещё завершать начатую операцию в фоне — следующий запуск дождётся её окончания"
                    : "Отменена пользователем");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"{actionLabel} ({processName})", "Ошибка запуска процесса", ex.Message);
            return new InstallResult(false, -1, ex.Message);
        }
    }

    private async Task<(int ExitCode, string StdErr)> RunOnceAsync(
        string fileName,
        string arguments,
        string actionLabel,
        string processName,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        _logger.Info($"{actionLabel} ({processName})", "Запущена, ожидание завершения");

        // Вывод читаем и отбрасываем: иначе заполненный канал может подвесить шумный установщик.
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var exitTask = process.WaitForExitAsync(cancellationToken);

        // Тихий установщик ничего не показывает: без счётчика непонятно, идёт установка или всё зависло.
        var stopwatch = Stopwatch.StartNew();
        var nextLog = HeartbeatLogInterval;
        using var timer = new PeriodicTimer(HeartbeatInterval);
        while (!exitTask.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.WhenAny(exitTask, timer.WaitForNextTickAsync(cancellationToken).AsTask());
            if (exitTask.IsCompleted)
            {
                break;
            }

            status?.Report(HeartbeatMessage(actionLabel, stopwatch.Elapsed));
            if (stopwatch.Elapsed >= nextLog)
            {
                _logger.Info($"{actionLabel} ({processName})", $"Выполняется уже {(int)stopwatch.Elapsed.TotalMinutes} мин");
                nextLog += HeartbeatLogInterval;
            }
        }

        await exitTask;
        await stdOutTask;
        var stdErr = await stdErrTask;
        return (process.ExitCode, stdErr);
    }

    private InstallResult LogResult((int ExitCode, string StdErr) run, string actionLabel, string processName, string? msiLogPath)
    {
        var success = run.ExitCode is 0 or ExitCodeRebootRequired;
        if (success)
        {
            _logger.Info($"{actionLabel} ({processName})",
                run.ExitCode == ExitCodeRebootRequired
                    ? "Успешно завершена, требуется перезагрузка"
                    : "Успешно завершена");
        }
        else if (run.ExitCode == ExitCodeFatalInstall && MsiLogShowsRebootRequired(msiLogPath))
        {
            // Пакет КриптоПро отказывается ставиться, пока Windows не перезагружена после удаления прошлой версии; в тихом режиме видно только код 1603.
            const string message = "Windows ждёт перезагрузки после удаления прошлой версии (установщик пакета отказывается ставиться до неё). Перезагрузите компьютер и повторите установку";
            _logger.Warning($"{actionLabel} ({processName})", $"Код завершения: {run.ExitCode}: {message}");
            return new InstallResult(false, run.ExitCode, message, RebootBeforeRetry: true);
        }
        else if (actionLabel == DefaultActionLabel)
        {
            _logger.Error($"{actionLabel} ({processName})", $"Код завершения: {run.ExitCode}", run.StdErr);
        }
        else
        {
            // Деинсталляторы бывают с нестандартными кодами (Chromium возвращает 19 при успехе): итог проверяется по реестру.
            _logger.Warning($"{actionLabel} ({processName})", $"Код завершения: {run.ExitCode} (результат проверяется по реестру)", run.StdErr);
        }

        return new InstallResult(success, run.ExitCode, success ? null : run.StdErr);
    }

    private async Task WaitForWindowsInstallerAsync(string actionLabel, string processName, IProgress<string>? status, CancellationToken cancellationToken)
    {
        if (!WindowsInstallerState.IsBusy())
        {
            return;
        }

        var action = $"{actionLabel} ({processName})";
        _logger.Warning(action, "Windows Installer занят другой установкой, ожидание её окончания");
        var stopwatch = Stopwatch.StartNew();

        while (WindowsInstallerState.IsBusy())
        {
            if (stopwatch.Elapsed > MaxInstallerWait)
            {
                _logger.Error(action, "Windows Installer не освободился за отведённое время, установка запускается как есть");
                return;
            }

            status?.Report($"Ждём окончания другой установки Windows: {FormatElapsed(stopwatch.Elapsed)}. " +
                           "Обычно это незавершённая операция КриптоПро или обновление Windows — окно трогать не нужно.");
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        _logger.Info(action, $"Windows Installer освободился через {FormatElapsed(stopwatch.Elapsed)}");
    }

    /// <summary>Добавляет подробный журнал msiexec рядом с журналом приложения: по нему видно, на каком действии пакет задержался.</summary>
    private (string Arguments, string? LogPath) WithMsiLog(string arguments, string actionLabel)
    {
        if (arguments.Contains(" /l", StringComparison.OrdinalIgnoreCase))
        {
            return (arguments, null);
        }

        try
        {
            var directory = _configService.ResolvePath(_configService.Current.Logging.LogDirectory);
            Directory.CreateDirectory(directory);
            var logPath = Path.Combine(directory, $"msi-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
            _logger.Info($"{actionLabel} (msiexec.exe)", $"Подробный журнал установщика: {logPath}");
            return ($"{arguments} /l*v \"{logPath}\"", logPath);
        }
        catch (Exception)
        {
            return (arguments, null);
        }
    }

    /// <summary>В журнале msiexec есть отказ пакета «сначала перезагрузка» (действие вида RebootRequiredFor…, а не обычная ошибка установки).</summary>
    private static bool MsiLogShowsRebootRequired(string? logPath)
    {
        if (logPath is null || !File.Exists(logPath))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, System.Text.Encoding.Unicode, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd().Contains("RebootRequiredFor", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string HeartbeatMessage(string actionLabel, TimeSpan elapsed)
    {
        var message = $"{actionLabel} выполняется: {FormatElapsed(elapsed)}.";
        return elapsed < TimeSpan.FromMinutes(3)
            ? message
            : $"{message} Пакеты КриптоПро CSP могут обрабатываться 20–30 минут — не закрывайте окно и не отменяйте шаг.";
    }

    private static string FormatElapsed(TimeSpan elapsed) => $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
}
