using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Работа с носителями электронной подписи (Rutoken, JaCarta): проверка драйверов и ожидание подключения.</summary>
public interface ITokenService
{
    ComponentInfo CheckRutokenDriver();

    ComponentInfo CheckJaCartaDriver();

    IReadOnlyList<TokenInfo> GetConnectedTokens();

    /// <summary>Асинхронно ожидает подключения носителя, опрашивая систему с интервалом из конфигурации.</summary>
    Task<TokenInfo?> WaitForTokenAsync(IProgress<string>? progress, CancellationToken cancellationToken = default);
}
