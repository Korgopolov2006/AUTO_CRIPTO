using System.Management;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="ITokenService"/>
public sealed class TokenService : ITokenService
{
    private const string RutokenDriverDisplayNamePattern = "Rutoken";
    private const string RutokenDriverDisplayNamePatternRu = "Рутокен";
    private const string JaCartaDriverDisplayNamePattern = "JaCarta";

    private readonly IRegistryService _registryService;
    private readonly IConfigService _configService;
    private readonly ILoggingService _logger;

    public TokenService(IRegistryService registryService, IConfigService configService, ILoggingService logger)
    {
        _registryService = registryService;
        _configService = configService;
        _logger = logger;
    }

    public ComponentInfo CheckRutokenDriver() => CheckDriver(
        ComponentType.RutokenDriver, "Драйвер Rutoken", "Драйвер для работы с носителями Rutoken",
        RutokenDriverDisplayNamePattern, RutokenDriverDisplayNamePatternRu);

    public ComponentInfo CheckJaCartaDriver() => CheckDriver(
        ComponentType.JaCartaDriver, "Драйвер JaCarta", "Драйвер для работы с носителями JaCarta",
        JaCartaDriverDisplayNamePattern);

    public IReadOnlyList<TokenInfo> GetConnectedTokens()
    {
        var tokens = new List<TokenInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE ConfigManagerErrorCode = 0");

            foreach (ManagementBaseObject device in searcher.Get())
            {
                var name = device["Name"] as string ?? string.Empty;
                var deviceId = device["DeviceID"] as string ?? string.Empty;

                var type = DetectTokenType(name);
                if (type == TokenType.Unknown)
                {
                    continue;
                }

                tokens.Add(new TokenInfo
                {
                    Type = type,
                    DeviceName = name,
                    SerialNumber = ExtractSerial(deviceId),
                    IsConnected = true,
                    DriverInstalled = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("Поиск подключенных носителей", "Ошибка обращения к WMI", ex.Message);
        }

        return tokens;
    }

    public async Task<TokenInfo?> WaitForTokenAsync(IProgress<string>? progress, CancellationToken cancellationToken = default)
    {
        var settings = _configService.Current.CheckSettings;
        var deadline = DateTime.Now.AddSeconds(settings.TokenWaitTimeoutSeconds);

        progress?.Report("Подключите носитель электронной подписи.");
        _logger.Info("Ожидание носителя", "Запрошено подключение носителя электронной подписи");

        while (DateTime.Now < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tokens = GetConnectedTokens();
            if (tokens.Count > 0)
            {
                _logger.Info("Ожидание носителя", $"Обнаружен носитель: {tokens[0].DeviceName}");
                return tokens[0];
            }

            await Task.Delay(settings.TokenPollIntervalMs, cancellationToken);
        }

        _logger.Warning("Ожидание носителя", "Носитель не был подключен в течение отведённого времени");
        return null;
    }

    private ComponentInfo CheckDriver(ComponentType type, string name, string description, params string[] displayNamePatterns)
    {
        var product = displayNamePatterns
            .Select(_registryService.FindInstalledProduct)
            .FirstOrDefault(p => p is not null);
        var installed = product is not null;

        return new ComponentInfo
        {
            Type = type,
            Name = name,
            InstalledVersion = product?.DisplayVersion,
            Description = description,
            Status = installed ? StatusLevel.Ok : StatusLevel.Warning,
            ErrorMessage = installed ? null : $"{name} не установлен"
        };
    }

    private static TokenType DetectTokenType(string deviceName)
    {
        if (deviceName.Contains("Rutoken", StringComparison.OrdinalIgnoreCase))
        {
            return TokenType.Rutoken;
        }

        if (deviceName.Contains("JaCarta", StringComparison.OrdinalIgnoreCase))
        {
            return TokenType.JaCarta;
        }

        return TokenType.Unknown;
    }

    private static string ExtractSerial(string deviceId)
    {
        var parts = deviceId.Split('\\');
        return parts.Length > 0 ? parts[^1] : deviceId;
    }
}
