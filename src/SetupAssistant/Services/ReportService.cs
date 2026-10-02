using System.IO;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IReportService"/>
public sealed class ReportService : IReportService
{
    private readonly ILoggingService _logger;

    public ReportService(ILoggingService logger)
    {
        _logger = logger;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public string Export(ReportModel report, ReportFormat format, string filePath)
    {
        try
        {
            switch (format)
            {
                case ReportFormat.Txt:
                    File.WriteAllText(filePath, BuildTextReport(report), Encoding.UTF8);
                    break;
                case ReportFormat.Pdf:
                    BuildPdfDocument(report).GeneratePdf(filePath);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }

            _logger.Info("Экспорт отчёта", $"Отчёт сохранён: {filePath}");
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.Error("Экспорт отчёта", "Ошибка формирования отчёта", ex.Message);
            throw;
        }
    }

    private static string BuildTextReport(ReportModel report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ОТЧЁТ О НАСТРОЙКЕ РАБОЧЕГО МЕСТА ДЛЯ ЭЛЕКТРОННОЙ ПОДПИСИ");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Дата формирования: {report.GeneratedAt:dd.MM.yyyy HH:mm:ss}");
        sb.AppendLine($"Длительность настройки: {report.ExecutionDuration:hh\\:mm\\:ss}");
        sb.AppendLine($"Итог: {(report.OverallSuccess ? "УСПЕШНО" : "ЕСТЬ ОШИБКИ")}");
        sb.AppendLine();

        sb.AppendLine("СВЕДЕНИЯ О КОМПЬЮТЕРЕ");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"Имя компьютера: {report.SystemInfo.ComputerName}");
        sb.AppendLine($"Пользователь: {report.SystemInfo.UserName}");
        sb.AppendLine($"Версия Windows: {report.SystemInfo.WindowsVersion}");
        sb.AppendLine($"Архитектура: {report.SystemInfo.Architecture}");
        sb.AppendLine();

        sb.AppendLine("УСТАНОВЛЕННОЕ ПО И КОМПОНЕНТЫ");
        sb.AppendLine(new string('-', 60));
        foreach (var component in report.Components)
        {
            sb.AppendLine($"[{component.Status}] {component.Name} — версия: {component.InstalledVersion ?? "не установлено"}" +
                           (component.RecommendedVersion is null ? string.Empty : $" (рекомендуется: {component.RecommendedVersion})"));
            if (!string.IsNullOrEmpty(component.ErrorMessage))
            {
                sb.AppendLine($"    Ошибка: {component.ErrorMessage}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("СЕРТИФИКАТЫ");
        sb.AppendLine(new string('-', 60));
        if (report.Certificates.Count == 0)
        {
            sb.AppendLine("Сертификаты не найдены.");
        }
        else
        {
            foreach (var cert in report.Certificates)
            {
                sb.AppendLine($"Владелец: {cert.Owner}; Организация: {cert.Organization}; Серийный номер: {cert.SerialNumber}; " +
                               $"Действителен до: {cert.ValidTo:dd.MM.yyyy}{(cert.IsExpired ? " (ПРОСРОЧЕН)" : string.Empty)}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("ОШИБКИ");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine(report.Errors.Count == 0 ? "Ошибок не обнаружено." : string.Join(Environment.NewLine, report.Errors));

        return sb.ToString();
    }

    private static IDocument BuildPdfDocument(ReportModel report)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Text("Отчёт о настройке рабочего места для электронной подписи")
                    .SemiBold().FontSize(16);

                page.Content().PaddingTop(10).Column(column =>
                {
                    column.Spacing(8);

                    column.Item().Text($"Дата формирования: {report.GeneratedAt:dd.MM.yyyy HH:mm:ss}");
                    column.Item().Text($"Длительность настройки: {report.ExecutionDuration:hh\\:mm\\:ss}");
                    column.Item().Text($"Итог: {(report.OverallSuccess ? "Успешно" : "Есть ошибки")}")
                        .FontColor(report.OverallSuccess ? Colors.Green.Darken2 : Colors.Red.Darken2).Bold();

                    column.Item().PaddingTop(10).Text("Сведения о компьютере").Bold().FontSize(12);
                    column.Item().Text($"Компьютер: {report.SystemInfo.ComputerName}    Пользователь: {report.SystemInfo.UserName}");
                    column.Item().Text($"Windows: {report.SystemInfo.WindowsVersion}    Архитектура: {report.SystemInfo.Architecture}");

                    column.Item().PaddingTop(10).Text("Компоненты").Bold().FontSize(12);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Компонент").Bold();
                            header.Cell().Text("Статус").Bold();
                            header.Cell().Text("Версия").Bold();
                            header.Cell().Text("Рекомендуемая").Bold();
                        });

                        foreach (var component in report.Components)
                        {
                            table.Cell().Text(component.Name);
                            table.Cell().Text(component.Status.ToString());
                            table.Cell().Text(component.InstalledVersion ?? "—");
                            table.Cell().Text(component.RecommendedVersion ?? "—");
                        }
                    });

                    column.Item().PaddingTop(10).Text("Сертификаты").Bold().FontSize(12);
                    if (report.Certificates.Count == 0)
                    {
                        column.Item().Text("Сертификаты не найдены.");
                    }
                    else
                    {
                        foreach (var cert in report.Certificates)
                        {
                            column.Item().Text($"{cert.Owner} ({cert.Organization}) — до {cert.ValidTo:dd.MM.yyyy}" +
                                                (cert.IsExpired ? " [ПРОСРОЧЕН]" : string.Empty));
                        }
                    }

                    column.Item().PaddingTop(10).Text("Ошибки").Bold().FontSize(12);
                    column.Item().Text(report.Errors.Count == 0 ? "Ошибок не обнаружено." : string.Join("\n", report.Errors));
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Помощник настройки рабочего места для электронной подписи");
                });
            });
        });
    }
}
