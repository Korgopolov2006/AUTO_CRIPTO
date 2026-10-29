using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Спрашивает у серверов производителей, какая версия дистрибутива сейчас актуальна. Файлы не скачиваются.</summary>
public interface IRemoteVersionService
{
    Task<IReadOnlyList<RemoteDistribution>> CheckAsync(CancellationToken cancellationToken = default);
}
