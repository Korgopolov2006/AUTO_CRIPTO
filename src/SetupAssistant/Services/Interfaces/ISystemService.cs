using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Получение сведений об операционной системе и рабочей станции.</summary>
public interface ISystemService
{
    SystemInfo GetSystemInfo();
}
