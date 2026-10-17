using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Оркестрация полного процесса настройки рабочего места: установка всех компонентов по шагам с проверкой каждого этапа.</summary>
public interface ISetupOrchestrationService
{
    /// <summary>
    /// Выполняет шаги по порядку. Шаги с <see cref="SetupStepModel.IsSelected"/> = false пропускаются;
    /// уже установленные компоненты не переустанавливаются, если не задано <see cref="SetupRunOptions.ForceReinstall"/>.
    /// </summary>
    Task RunSetupAsync(IReadOnlyList<SetupStepModel> steps, IProgress<SetupStepModel>? progress, SetupRunOptions? options = null, CancellationToken cancellationToken = default);
}
