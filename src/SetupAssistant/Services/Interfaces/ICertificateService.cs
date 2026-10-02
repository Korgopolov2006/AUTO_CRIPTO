using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Получение и установка сертификатов электронной подписи из хранилищ Windows.</summary>
public interface ICertificateService
{
    IReadOnlyList<CertificateInfo> GetCertificates();

    Task<bool> InstallCertificateAsync(CertificateInfo certificate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Устанавливает сертификаты из всех доступных контейнеров (токены, реестр) в хранилище «Личное»
    /// текущего пользователя с привязкой к закрытому ключу (csptest -absorb) и сверяет результат по хранилищу.
    /// </summary>
    Task<TokenCertificateImportResult> ImportFromContainersAsync(CancellationToken cancellationToken = default);

    ComponentInfo CheckCertificatesStatus();
}
