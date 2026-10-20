using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <param name="RemovePersonalCertificates">
/// Удалять ли из хранилища «Личное» сертификаты, привязанные к ключам КриптоПро. Сами ключи, контейнеры и содержимое токена не затрагиваются.
/// </param>
public sealed record UninstallOptions(bool RemovePersonalCertificates);

/// <summary>Откат настройки рабочего места: удаление установленных компонентов и следов приложения.</summary>
public interface IUninstallService
{
    /// <summary>Сканирует компьютер (ничего не меняя) и возвращает шаги отката с описанием найденного.</summary>
    IReadOnlyList<SetupStepModel> BuildPlan(UninstallOptions options);

    /// <summary>Шаг относится к группе Континент-АП (Континент и связанные с ним компоненты, например КБ CSP).</summary>
    bool IsKontinentStep(SetupStepModel step);

    /// <summary>Выполняет шаги по порядку. Ошибка одного шага не останавливает остальные.</summary>
    Task RunAsync(IReadOnlyList<SetupStepModel> steps, IProgress<SetupStepModel>? progress, CancellationToken cancellationToken = default);
}
