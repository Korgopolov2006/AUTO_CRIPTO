using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Сравнение версий ПО и определение статуса совместимости относительно минимально допустимой/рекомендуемой версии.</summary>
public interface IVersionService
{
    /// <summary>Сравнивает две версии в свободном формате (не только System.Version). Возврат: -1, 0, 1.</summary>
    int Compare(string? versionA, string? versionB);

    /// <summary>Определяет статус компонента исходя из установленной, минимальной и рекомендуемой версии.</summary>
    StatusLevel EvaluateStatus(string? installedVersion, string? minVersion, string? recommendedVersion);
}
