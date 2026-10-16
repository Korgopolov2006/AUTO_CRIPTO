using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Формирование итогового отчёта по результатам настройки/диагностики рабочего места.</summary>
public interface IReportService
{
    string Export(ReportModel report, ReportFormat format, string filePath);
}
