using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Предварительные проверки перед установкой и удалением: права, место на диске, дистрибутивы, сеть, ожидающая перезагрузка.</summary>
public interface IPreflightService
{
    Task<IReadOnlyList<PreflightCheck>> RunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверки перед удалением: права, место на диске, занятый установщик Windows, ожидающая перезагрузка, есть ли что удалять;
    /// для полной очистки ещё и доступность папки отчёта (в неё пишутся резервные копии).
    /// </summary>
    Task<IReadOnlyList<PreflightCheck>> RunForUninstallAsync(bool fullCleanup, CancellationToken cancellationToken = default);
}
