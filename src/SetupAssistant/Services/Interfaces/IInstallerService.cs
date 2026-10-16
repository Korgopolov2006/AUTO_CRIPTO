using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Результат выполнения тихой установки.</summary>
public sealed record InstallResult(bool Success, int ExitCode, string? ErrorMessage)
{
    /// <summary>Установка прошла, но для завершения требуется перезагрузка (код 3010).</summary>
    public bool RebootRequired => ExitCode == 3010;
}

/// <summary>Установка MSI/EXE пакетов в тихом (silent) режиме.</summary>
public interface IInstallerService
{
    /// <summary>Устанавливает пакет по его описанию из конфигурации (тип и ключи тихой установки).</summary>
    Task<InstallResult> InstallAsync(InstallerDefinition installer, CancellationToken cancellationToken = default);

    Task<InstallResult> InstallMsiAsync(string msiPath, string additionalArgs = "", CancellationToken cancellationToken = default);

    Task<InstallResult> InstallExeAsync(string exePath, string silentArgs = "", CancellationToken cancellationToken = default);
}
