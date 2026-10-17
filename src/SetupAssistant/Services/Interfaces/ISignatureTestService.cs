using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Обновление одного этапа теста подписи. Stage: component, certificate, sign, verify.</summary>
public sealed record SignatureStageUpdate(string Stage, StatusLevel Status, string Details, string? Advice = null);

/// <summary>Итог теста подписи.</summary>
public sealed record SignatureTestResult(bool Success, string Summary);

/// <summary>
/// Сквозная проверка электронной подписи тем же путём, каким подписывает браузерный плагин: КриптоПро CAdESCOM.
/// Подписывает тестовые данные выбранным сертификатом и проверяет подпись. Ничего не сохраняет и не отправляет.
/// </summary>
public interface ISignatureTestService
{
    /// <summary>Выполняет тест; этапы по очереди сообщаются в <paramref name="progress"/>.</summary>
    Task<SignatureTestResult> RunAsync(string thumbprint, IProgress<SignatureStageUpdate> progress, CancellationToken cancellationToken = default);
}
