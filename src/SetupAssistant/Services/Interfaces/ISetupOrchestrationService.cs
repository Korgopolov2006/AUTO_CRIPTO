using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Оркестрация полного процесса настройки рабочего места: установка всех компонентов по шагам с проверкой каждого этапа.</summary>
public interface ISetupOrchestrationService
{
    Task RunSetupAsync(IReadOnlyList<SetupStepModel> steps, IProgress<SetupStepModel>? progress, CancellationToken cancellationToken = default);
}
