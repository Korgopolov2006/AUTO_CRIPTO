namespace SetupAssistant.Services.Interfaces;

/// <summary>Сведения об установленном ПО, найденные через ветки реестра Windows Uninstall.</summary>
/// <param name="RegistryPath">Полный путь записи в реестре (например, <c>HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Имя</c>).</param>
public sealed record InstalledProductInfo(
    string DisplayName, string? DisplayVersion, string? InstallLocation, string? UninstallString, string? KeyName = null, string? RegistryPath = null);

/// <summary>Проверка наличия установленного ПО через реестр Windows.</summary>
public interface IRegistryService
{
    /// <summary>Ищет установленный продукт по частичному совпадению отображаемого имени (регистронезависимо).</summary>
    InstalledProductInfo? FindInstalledProduct(string displayNamePattern);

    /// <summary>Возвращает все установленные продукты, зарегистрированные в разделах Uninstall (для 32 и 64-бит).</summary>
    IReadOnlyList<InstalledProductInfo> GetAllInstalledProducts();

    /// <summary>
    /// Удаляет запись продукта из раздела Uninstall («Программы и компоненты»). Только запись: файлов и деинсталлятора это не касается.
    /// Нужно, когда программа уже удалена, а запись осталась и мешает (деинсталлятора нет). false — путь не из раздела Uninstall или удалить не удалось.
    /// </summary>
    bool RemoveUninstallEntry(InstalledProductInfo product);

    /// <summary>Читает значение строкового параметра реестра, либо null если ключ/значение отсутствует.</summary>
    string? ReadValue(string hive, string subKey, string valueName);
}
