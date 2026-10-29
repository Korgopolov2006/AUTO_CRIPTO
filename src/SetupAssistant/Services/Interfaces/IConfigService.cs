using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Загрузка и предоставление конфигурации приложения из Config/Config.json.</summary>
public interface IConfigService
{
    AppConfig Current { get; }

    /// <summary>Откуда загружена конфигурация: файл рядом с exe, данные внутри единого exe или значения по умолчанию.</summary>
    string ConfigSource { get; }

    /// <summary>Загружает конфигурацию с диска. Если файл отсутствует или повреждён — создаёт конфигурацию по умолчанию.</summary>
    AppConfig Load();

    /// <summary>Возвращает абсолютный путь для относительного пути, указанного в конфигурации.</summary>
    string ResolvePath(string relativePath);

    /// <summary>Файл, в который сохраняются правки: Config\Config.json рядом с exe. Он важнее конфигурации внутри exe.</summary>
    string EditableConfigPath { get; }

    /// <summary>Размер дистрибутива без распаковки (рядом с exe или внутри него); null, если файла нет.</summary>
    long? GetDistributionSize(string relativePath);

    /// <summary>
    /// Вносит правки в Config.json: читает текущий текст, меняет только указанные значения (комментарии остаются), проверяет
    /// результат, сохраняет резервную копию Config.json.bak и загружает конфигурацию заново.
    /// </summary>
    /// <returns>Пути правок, которых не оказалось в файле (они не применены).</returns>
    IReadOnlyList<string> Update(IEnumerable<SetupAssistant.Helpers.ConfigEdit> edits);
}
