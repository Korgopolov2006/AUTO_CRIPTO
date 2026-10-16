using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Установка корневых и промежуточных сертификатов УЦ, чтобы Windows строила цепочку доверия сертификатов подписи.</summary>
public interface ICaCertificateService
{
    /// <summary>Сколько сертификатов УЦ описано в конфигурации.</summary>
    int ConfiguredCount { get; }

    /// <summary>
    /// Импортирует сертификаты из конфигурации в указанные хранилища. Каждый сертификат перед импортом проверяется:
    /// отпечаток должен совпасть с заданным в конфигурации, срок действия — не истёк, сертификат — быть сертификатом УЦ.
    /// </summary>
    Task<InstallResult> InstallAsync(CancellationToken cancellationToken = default);

    /// <summary>Проверяет, что все настроенные сертификаты УЦ присутствуют в своих хранилищах.</summary>
    ComponentInfo Check();
}
