namespace SetupAssistant.Services.Interfaces;

/// <summary>Сведения об установленном ПО, найденные через ветки реестра Windows Uninstall.</summary>
public sealed record InstalledProductInfo(string DisplayName, string? DisplayVersion, string? InstallLocation, string? UninstallString);

/// <summary>Проверка наличия установленного ПО через реестр Windows.</summary>
public interface IRegistryService
{
    /// <summary>Ищет установленный продукт по частичному совпадению отображаемого имени (регистронезависимо).</summary>
    InstalledProductInfo? FindInstalledProduct(string displayNamePattern);

    /// <summary>Возвращает все установленные продукты, зарегистрированные в разделах Uninstall (для 32 и 64-бит).</summary>
    IReadOnlyList<InstalledProductInfo> GetAllInstalledProducts();

    /// <summary>Читает значение строкового параметра реестра, либо null если ключ/значение отсутствует.</summary>
    string? ReadValue(string hive, string subKey, string valueName);
}
