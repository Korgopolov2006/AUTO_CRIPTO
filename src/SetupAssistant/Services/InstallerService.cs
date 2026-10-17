using System.Diagnostics;
using System.IO;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IInstallerService"/>
public sealed class InstallerService : IInstallerService
{
    /// <summary>Код msiexec/установщиков «успешно, требуется перезагрузка».</summary>
    private const int ExitCodeRebootRequired = 3010;

    private readonly IConfigService _configService;
    private readonly ILoggingService _logger;

    public InstallerService(IConfigService configService, ILoggingService logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public async Task<InstallResult> InstallAsync(InstallerDefinition installer, CancellationToken cancellationToken = default)
    {
        // В едином exe крупный дистрибутив распаковывается при первом обращении — не блокируем окно.
        var fullPath = await Task.Run(() => _configService.ResolvePath(installer.Path), cancellationToken);

        return installer.Kind == InstallerKind.Msi
            ? await InstallMsiAsync(fullPath, installer.SilentArgs, cancellationToken)
            : await InstallExeAsync(fullPath, installer.SilentArgs, cancellationToken);
    }

    public async Task<InstallResult> InstallMsiAsync(string msiPath, string additionalArgs = "", CancellationToken cancellationToken = default)
    {
        if (!File.Exists(msiPath))
        {
            var message = $"Файл установщика не найден: {msiPath}";
            _logger.Error("Установка MSI", "Не выполнена", message);
            return new InstallResult(false, -1, message);
        }

        var args = $"/i \"{msiPath}\" /qn /norestart {additionalArgs}".Trim();
        return await RunProcessAsync("msiexec.exe", args, cancellationToken);
    }

    public async Task<InstallResult> InstallExeAsync(string exePath, string silentArgs = "", CancellationToken cancellationToken = default)
    {
        if (!File.Exists(exePath))
        {
            var message = $"Файл установщика не найден: {exePath}";
            _logger.Error("Установка EXE", "Не выполнена", message);
            return new InstallResult(false, -1, message);
        }

        return await RunProcessAsync(exePath, silentArgs, cancellationToken);
    }

    public Task<InstallResult> RunAsync(string fileName, string arguments, string actionLabel, CancellationToken cancellationToken = default) =>
        RunProcessAsync(fileName, arguments, cancellationToken, actionLabel);

    private const string DefaultActionLabel = "Установка";

    private async Task<InstallResult> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken, string actionLabel = DefaultActionLabel)
    {
        var processName = Path.GetFileName(fileName);

        try
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

            // Вывод читаем и отбрасываем: иначе заполненный канал может подвесить шумный установщик.
            var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await stdOutTask;
            var stdErr = await stdErrTask;

            var success = process.ExitCode is 0 or ExitCodeRebootRequired;
            if (success)
            {
                _logger.Info($"{actionLabel} ({processName})",
                    process.ExitCode == ExitCodeRebootRequired
                        ? "Успешно завершена, требуется перезагрузка"
                        : "Успешно завершена");
            }
            else if (actionLabel == DefaultActionLabel)
            {
                _logger.Error($"{actionLabel} ({processName})", $"Код завершения: {process.ExitCode}", stdErr);
            }
            else
            {
                // Деинсталляторы бывают с нестандартными кодами (Chromium возвращает 19 при успехе): итог проверяется по реестру.
                _logger.Warning($"{actionLabel} ({processName})", $"Код завершения: {process.ExitCode} (результат проверяется по реестру)", stdErr);
            }

            return new InstallResult(success, process.ExitCode, success ? null : stdErr);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning($"{actionLabel} ({processName})", "Отменена пользователем");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"{actionLabel} ({processName})", "Ошибка запуска процесса", ex.Message);
            return new InstallResult(false, -1, ex.Message);
        }
    }
}
