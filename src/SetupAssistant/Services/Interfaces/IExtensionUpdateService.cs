using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Проверка, не вышла ли в Chrome Web Store версия новее той, что лежит локально в .crx.</summary>
public interface IExtensionUpdateService
{
    Task<IReadOnlyList<ExtensionUpdateInfo>> CheckAsync(CancellationToken cancellationToken = default);
}
