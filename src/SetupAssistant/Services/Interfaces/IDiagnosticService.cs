using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Полная диагностика рабочего места без установки компонентов.</summary>
public interface IDiagnosticService
{
    Task<IReadOnlyList<ComponentInfo>> RunFullDiagnosticAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
