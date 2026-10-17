using System.IO;
using System.Text.Json;
using SetupAssistant.Helpers;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IConfigService"/>
/// <remarks>
/// Файлы ищутся рядом с exe (так можно подменить конфиг или дистрибутив), а если их там нет — берутся из данных,
/// дописанных в конец единого exe, и распаковываются в кэш только по требованию.
/// </remarks>
public sealed class ConfigService : IConfigService, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly string _baseDirectory;
    private readonly string _cacheRoot;
    private readonly string _configFilePath;
    private readonly PayloadStore? _payload;

    public AppConfig Current { get; private set; }

    public string ConfigSource { get; private set; } = "значения по умолчанию";

    public ConfigService()
        : this(
            Environment.ProcessPath,
            AppContext.BaseDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SetupAssistant", "Payload"))
    {
    }

    internal ConfigService(string? exePath, string baseDirectory, string cacheRoot)
    {
        _baseDirectory = baseDirectory;
        _cacheRoot = cacheRoot;
        _configFilePath = Path.Combine(baseDirectory, "Config", "Config.json");
        _payload = PayloadStore.TryOpen(exePath);
        RemoveOutdatedCaches();
        Current = Load();
    }

    /// <summary>Удаляет кэши распаковки от предыдущих сборок exe, чтобы они не копились в ProgramData.</summary>
    private void RemoveOutdatedCaches()
    {
        if (_payload is null || !Directory.Exists(_cacheRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(_cacheRoot))
        {
            if (string.Equals(Path.GetFileName(directory), _payload.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception)
            {
                // Кэш занят другим экземпляром или правами — оставляем до следующего запуска.
            }
        }
    }

    public AppConfig Load()
    {
        try
        {
            string? json = null;
            var source = string.Empty;

            if (File.Exists(_configFilePath))
            {
                json = File.ReadAllText(_configFilePath);
                source = $"файл {_configFilePath}";
            }
            else if (_payload?.ReadAllText(@"Config\Config.json") is { } embedded)
            {
                json = embedded;
                source = $"данные внутри exe ({_payload.FileCount} файлов, {_payload.TotalSize / 1024 / 1024} МБ)";
            }

            if (json is not null)
            {
                var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
                if (config is not null)
                {
                    Current = config;
                    ConfigSource = source;
                    return config;
                }
            }
        }
        catch (Exception)
        {
            // Файл повреждён или недоступен — используем конфигурацию по умолчанию.
        }

        Current = new AppConfig();
        ConfigSource = "значения по умолчанию (конфигурация не найдена или повреждена)";
        return Current;
    }

    public string ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return _baseDirectory;
        }

        var expanded = Environment.ExpandEnvironmentVariables(relativePath);
        if (Path.IsPathRooted(expanded))
        {
            return expanded;
        }

        var nextToExe = Path.Combine(_baseDirectory, expanded);
        if (File.Exists(nextToExe) || Directory.Exists(nextToExe))
        {
            return nextToExe;
        }

        try
        {
            return _payload?.Extract(expanded, _cacheRoot) ?? nextToExe;
        }
        catch (Exception)
        {
            return nextToExe;
        }
    }

    public void Dispose() => _payload?.Dispose();
}
