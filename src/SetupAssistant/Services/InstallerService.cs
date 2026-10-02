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

    public Task<InstallResult> InstallAsync(InstallerDefinition installer, CancellationToken cancellationToken = default)
    {
        var fullPath = _configService.ResolvePath(installer.Path);

        return installer.Kind == InstallerKind.Msi
            ? InstallMsiAsync(fullPath, installer.SilentArgs, cancellationToken)
            : InstallExeAsync(fullPath, installer.SilentArgs, cancellationToken);
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

    private async Task<InstallResult> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
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

            var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdErr = await stdErrTask;

            var success = process.ExitCode is 0 or ExitCodeRebootRequired;
            if (success)
            {
                _logger.Info($"Установка ({processName})",
                    process.ExitCode == ExitCodeRebootRequired
                        ? "Успешно завершена, требуется перезагрузка"
                        : "Успешно завершена");
            }
            else
            {
                _logger.Error($"Установка ({processName})", $"Код завершения: {process.ExitCode}", stdErr);
            }

            return new InstallResult(success, process.ExitCode, success ? null : stdErr);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning($"Установка ({processName})", "Отменена пользователем");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Установка ({processName})", "Ошибка запуска процесса", ex.Message);
            return new InstallResult(false, -1, ex.Message);
        }
    }
}
