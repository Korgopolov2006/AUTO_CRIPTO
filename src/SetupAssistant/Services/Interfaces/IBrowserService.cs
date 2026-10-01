using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Проверка наличия и версий поддерживаемых браузеров (расширяемо через Config.SupportedBrowsers).</summary>
public interface IBrowserService
{
    /// <summary>Проверяет статус конкретного браузера по его описанию из конфигурации.</summary>
    ComponentInfo CheckBrowser(BrowserDefinition browser);

    /// <summary>Проверяет все браузеры, перечисленные в конфигурации.</summary>
    IReadOnlyList<ComponentInfo> CheckAllBrowsers();

    /// <summary>Проверяет наличие и версию Контур.Плагина (устанавливается отдельно от браузера).</summary>
    ComponentInfo CheckKonturPlugin();

    /// <summary>Проверяет «Плагин пользователя систем электронного правительства» (native-хост ru.rtlabs.ifcplugin для расширения Госуслуг).</summary>
    ComponentInfo CheckGosuslugiPlugin();
}
