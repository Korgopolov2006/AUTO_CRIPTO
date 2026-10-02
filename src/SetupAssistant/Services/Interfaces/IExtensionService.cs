using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>
/// Офлайн-установка и проверка расширений браузеров из локальных .crx-файлов
/// через политику принудительной установки Chromium (ExtensionInstallForcelist).
/// </summary>
public interface IExtensionService
{
    /// <summary>Копирует .crx в локальный репозиторий, формирует updates.xml и применяет политику ко всем настроенным браузерам.</summary>
    Task<InstallResult> InstallExtensionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Проверяет каждое расширение из конфигурации: наличие в политике и наличие .crx в репозитории.</summary>
    IReadOnlyList<ComponentInfo> CheckAllExtensions();

    ComponentInfo CheckCryptoProExtension();

    ComponentInfo CheckKonturExtension();
}
