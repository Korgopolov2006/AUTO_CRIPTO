using System.IO;
using System.Text.Json;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IConfigService"/>
public sealed class ConfigService : IConfigService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly string _configFilePath;

    public AppConfig Current { get; private set; }

    public ConfigService()
    {
        _configFilePath = Path.Combine(AppContext.BaseDirectory, "Config", "Config.json");
        Current = Load();
    }

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
                if (config is not null)
                {
                    Current = config;
                    return config;
                }
            }
        }
        catch (Exception)
        {
            // Файл повреждён или недоступен — используем конфигурацию по умолчанию.
        }

        Current = new AppConfig();
        return Current;
    }

    public string ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return AppContext.BaseDirectory;
        }

        var expanded = Environment.ExpandEnvironmentVariables(relativePath);

        return Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(AppContext.BaseDirectory, expanded);
    }
}
