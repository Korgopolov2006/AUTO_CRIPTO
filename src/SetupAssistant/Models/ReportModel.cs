namespace SetupAssistant.Models;

/// <summary>Итоговая модель отчета по настройке/диагностике рабочего места для экспорта в TXT/PDF.</summary>
public sealed class ReportModel
{
    public SystemInfo SystemInfo { get; set; } = new();
    public List<ComponentInfo> Components { get; set; } = new();
    public List<CertificateInfo> Certificates { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public TimeSpan ExecutionDuration { get; set; }
    public bool OverallSuccess => Errors.Count == 0 && Components.All(c => c.Status is not StatusLevel.Error);
}
