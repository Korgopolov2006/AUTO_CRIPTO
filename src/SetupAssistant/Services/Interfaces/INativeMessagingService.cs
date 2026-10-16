using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Проверка компонентов, через которые расширения браузера общаются с плагинами электронной подписи.</summary>
public interface INativeMessagingService
{
    /// <summary>Для каждого хоста из конфигурации проверяет регистрацию в реестре и наличие файла манифеста.</summary>
    IReadOnlyList<ComponentInfo> CheckHosts();

    /// <summary>Проверяет регистрацию COM-компонента CAdESCOM (КриптоПро ЭЦП Browser plug-in).</summary>
    ComponentInfo CheckCadesCom();
}
