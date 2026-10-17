namespace SetupAssistant.Services.Interfaces;

/// <summary>Загрузка актуальных дистрибутивов из интернета.</summary>
public interface IDownloadService
{
    /// <summary>
    /// Скачивает файл во временную папку и возвращает путь к нему; null — если загрузка не удалась.
    /// Если задан <paramref name="githubAssetRegex"/>, <paramref name="url"/> считается адресом
    /// GitHub API «latest release», а файл выбирается по имени ассета.
    /// </summary>
    Task<string?> DownloadAsync(
        string url,
        string? githubAssetRegex,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Имя файла в последнем релизе GitHub (<paramref name="apiUrl"/> — адрес «latest release», файл выбирается по
    /// <paramref name="assetRegex"/>), ничего не скачивая; null — если узнать не удалось (нет связи).
    /// </summary>
    Task<string?> GetLatestAssetNameAsync(string apiUrl, string assetRegex, CancellationToken cancellationToken = default);
}
