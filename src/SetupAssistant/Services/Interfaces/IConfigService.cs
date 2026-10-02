using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Загрузка и предоставление конфигурации приложения из Config/Config.json.</summary>
public interface IConfigService
{
    AppConfig Current { get; }

    /// <summary>Загружает конфигурацию с диска. Если файл отсутствует или повреждён — создаёт конфигурацию по умолчанию.</summary>
    AppConfig Load();

    /// <summary>Возвращает абсолютный путь для относительного пути, указанного в конфигурации.</summary>
    string ResolvePath(string relativePath);
}
