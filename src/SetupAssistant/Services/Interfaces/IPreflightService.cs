using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Предварительные проверки перед установкой: права, место на диске, дистрибутивы, сеть, ожидающая перезагрузка.</summary>
public interface IPreflightService
{
    Task<IReadOnlyList<PreflightCheck>> RunAsync(CancellationToken cancellationToken = default);
}
