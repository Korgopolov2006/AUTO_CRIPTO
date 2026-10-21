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

    /// <summary>
    /// Откатывает то, что успело установиться в последнем запуске (в том числе прерванную установку): удаляет эти компоненты и
    /// временные файлы. Компоненты, стоявшие до запуска, не затрагиваются. <paramref name="status"/> получает текст о ходе отката.
    /// </summary>
    Task<RollbackSummary> RollbackAsync(IReadOnlyList<SetupStepModel> steps, IProgress<string>? status, CancellationToken cancellationToken = default);
}

/// <param name="Removed">Откачено компонентов.</param>
/// <param name="Failed">Не удалось откатить компонентов.</param>
/// <param name="NotSupported">Названия компонентов, которые приложение откатывать не умеет (удалить вручную).</param>
public sealed record RollbackSummary(int Removed, int Failed, bool NeedsReboot, IReadOnlyList<string> NotSupported);
