using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="ISupportBundleService"/>
public sealed class SupportBundleService : ISupportBundleService
{
    private static readonly TimeSpan LogAge = TimeSpan.FromDays(7);

    private readonly IWorkplaceStatusService _statusService;
    private readonly IReportService _reportService;
    private readonly ICertificateService _certificateService;
    private readonly ISystemService _systemService;
    private readonly IConfigService _configService;
    private readonly ILoggingService _logger;

    public SupportBundleService(
        IWorkplaceStatusService statusService,
        IReportService reportService,
        ICertificateService certificateService,
        ISystemService systemService,
        IConfigService configService,
        ILoggingService logger)
    {
        _statusService = statusService;
        _reportService = reportService;
        _certificateService = certificateService;
        _systemService = systemService;
        _configService = configService;
        _logger = logger;
    }

    public async Task<string> CreateAsync(string zipPath, bool includeCertificates, CancellationToken cancellationToken = default)
    {
        // Свежая диагностика — основа пакета. Запускается из потока интерфейса (как и везде), поэтому до Task.Run.
        var summary = _statusService.CheckedAt is null || _statusService.IsChecking
            ? await _statusService.RefreshAsync(cancellationToken)
            : _statusService.Summary;

        return await Task.Run(() => Create(zipPath, includeCertificates, summary), cancellationToken);
    }

    private string Create(string zipPath, bool includeCertificates, ReadinessSummary summary)
    {
        var temp = Path.Combine(Path.GetTempPath(), "SetupAssistant-support-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);

        try
        {
            var systemInfo = _systemService.GetSystemInfo();
            var report = new ReportModel
            {
                SystemInfo = systemInfo,
                Components = summary.Results.ToList(),
                Certificates = includeCertificates ? _certificateService.GetCertificates().ToList() : new List<CertificateInfo>(),
                Errors = summary.Problems.Select(c => $"{c.Name}: {c.DashboardText}").ToList()
            };

            var diagnosticsPath = Path.Combine(temp, "diagnostics.txt");
            _reportService.Export(report, ReportFormat.Txt, diagnosticsPath);

            var directory = Path.GetDirectoryName(Path.GetFullPath(zipPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                AddText(zip, "README.txt", BuildReadme(systemInfo, includeCertificates));
                zip.CreateEntryFromFile(diagnosticsPath, "diagnostics.txt");
                AddText(zip, "overview.txt", BuildOverview(summary));
                AddText(zip, "Config.json", JsonSerializer.Serialize(_configService.Current, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                AddLogs(zip);
            }

            _logger.Info("Пакет для поддержки", $"Пакет создан: {zipPath}" + (includeCertificates ? " (со сведениями о сертификатах)" : string.Empty));
            return zipPath;
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException)
            {
                // Временная папка удалится системой позже.
            }
        }
    }

    private string BuildReadme(SystemInfo system, bool includeCertificates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ПАКЕТ ДЛЯ ПОДДЕРЖКИ");
        sb.AppendLine(new string('=', 40));
        sb.AppendLine($"Создан: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
        sb.AppendLine($"Приложение: {_configService.Current.AppName}, версия {_configService.Current.AppVersion}");
        sb.AppendLine($"Среда: {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Компьютер: {system.ComputerName}; пользователь: {system.UserName}; права администратора: {(system.IsAdministrator ? "да" : "нет")}");
        sb.AppendLine();
        sb.AppendLine("Состав пакета:");
        sb.AppendLine("  diagnostics.txt — результаты диагностики и версии компонентов");
        sb.AppendLine("  overview.txt    — что установлено, что нет, домен");
        sb.AppendLine("  Config.json     — конфигурация приложения");
        sb.AppendLine("  logs\\           — журналы приложения за последние 7 дней");
        sb.AppendLine();
        sb.AppendLine("Чего в пакете нет: PIN-кодов, паролей, закрытых ключей и содержимого токена.");
        sb.AppendLine(includeCertificates
            ? "Сертификаты: ВКЛЮЧЕНЫ в diagnostics.txt (владелец, организация, серийный номер, срок) — это персональные данные."
            : "Сертификаты: не включены.");
        return sb.ToString();
    }

    private static string BuildOverview(ReadinessSummary summary)
    {
        var sb = new StringBuilder();
        foreach (var item in summary.Results.Where(c => c.ShowOnDashboard || c.IsDeviceManagement))
        {
            sb.AppendLine($"{item.Name}: {item.DashboardText}");
        }

        sb.AppendLine();
        sb.AppendLine($"Предупреждений: {summary.WarningCount}, ошибок: {summary.ErrorCount}, к сведению: {summary.InfoCount}");
        return sb.ToString();
    }

    private void AddLogs(ZipArchive zip)
    {
        var logDirectory = Path.GetDirectoryName(_logger.CurrentLogFilePath);
        if (logDirectory is null || !Directory.Exists(logDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(logDirectory, "*.log").Where(f => DateTime.Now - File.GetLastWriteTime(f) <= LogAge))
        {
            try
            {
                // Файл текущего журнала открыт на запись самим приложением — читаем с общим доступом.
                using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var entry = zip.CreateEntry("logs/" + Path.GetFileName(file), CompressionLevel.Optimal);
                using var target = entry.Open();
                source.CopyTo(target);
            }
            catch (IOException)
            {
                // Журнал, который не удалось прочитать, пропускаем — остальной пакет всё равно полезен.
            }
        }
    }

    private static void AddText(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.Write(text);
    }
}
