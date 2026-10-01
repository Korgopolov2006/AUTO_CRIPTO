namespace SetupAssistant.Models;

/// <summary>Сведения об аппаратном/программном окружении рабочей станции.</summary>
public sealed class SystemInfo
{
    public string ComputerName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string WindowsVersion { get; set; } = string.Empty;
    public string WindowsEdition { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public bool IsAdministrator { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.Now;
}
