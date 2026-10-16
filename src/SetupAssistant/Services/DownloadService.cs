using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IDownloadService"/>
public sealed class DownloadService : IDownloadService, IDisposable
{
    private readonly ILoggingService _logger;
    private readonly HttpClient _http;

    public DownloadService(ILoggingService logger)
    {
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SetupAssistant/1.0");
    }

    public async Task<string?> DownloadAsync(
        string url,
        string? githubAssetRegex,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var downloadUrl = string.IsNullOrWhiteSpace(githubAssetRegex)
                ? url
                : await ResolveGitHubAssetAsync(url, githubAssetRegex, cancellationToken);

            using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                           ?? Path.GetFileName(response.RequestMessage?.RequestUri?.LocalPath ?? downloadUrl);
            fileName = Path.GetFileName(fileName);

            var directory = Path.Combine(Path.GetTempPath(), "SetupAssistant", "Downloads");
            Directory.CreateDirectory(directory);
            var targetPath = Path.Combine(directory, fileName);
            var partPath = targetPath + ".part";

            var total = response.Content.Headers.ContentLength ?? -1;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, n), cancellationToken);
                    read += n;
                    if (total > 0) progress?.Report((double)read / total);
                }
            }

            File.Move(partPath, targetPath, overwrite: true);
            _logger.Info("Загрузка дистрибутива", $"Скачано: {fileName} ({new FileInfo(targetPath).Length / 1024 / 1024} МБ)");
            return targetPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("Загрузка дистрибутива", "Не выполнена", $"{url}: {ex.Message}");
            return null;
        }
    }

    private async Task<string> ResolveGitHubAssetAsync(string apiUrl, string assetRegex, CancellationToken cancellationToken)
    {
        var json = await _http.GetStringAsync(apiUrl, cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var pattern = new Regex(assetRegex, RegexOptions.IgnoreCase);

        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            if (pattern.IsMatch(asset.GetProperty("name").GetString() ?? string.Empty))
            {
                return asset.GetProperty("browser_download_url").GetString()!;
            }
        }

        throw new InvalidOperationException($"В последнем релизе нет файла, подходящего под '{assetRegex}'");
    }

    public void Dispose() => _http.Dispose();
}
