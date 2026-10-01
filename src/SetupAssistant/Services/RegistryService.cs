using Microsoft.Win32;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IRegistryService"/>
public sealed class RegistryService : IRegistryService
{
    private static readonly string[] UninstallSubKeys =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    public InstalledProductInfo? FindInstalledProduct(string displayNamePattern)
    {
        if (string.IsNullOrWhiteSpace(displayNamePattern))
        {
            return null;
        }

        // Сравнение без учёта пробелов и дефисов: "Chromium-Gost" == "Chromium Gost" == "ChromiumGost".
        var normalizedPattern = Normalize(displayNamePattern);

        return GetAllInstalledProducts()
            .FirstOrDefault(p => Normalize(p.DisplayName).Contains(normalizedPattern, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value) =>
        value.Replace(" ", string.Empty).Replace("-", string.Empty);

    public IReadOnlyList<InstalledProductInfo> GetAllInstalledProducts()
    {
        var results = new List<InstalledProductInfo>();

        foreach (var subKeyPath in UninstallSubKeys)
        {
            CollectFromHive(Registry.LocalMachine, subKeyPath, results);
        }

        CollectFromHive(Registry.CurrentUser, UninstallSubKeys[0], results);

        return results;
    }

    public string? ReadValue(string hive, string subKey, string valueName)
    {
        try
        {
            var baseKey = hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase)
                ? Registry.CurrentUser
                : Registry.LocalMachine;

            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void CollectFromHive(RegistryKey baseKey, string subKeyPath, ICollection<InstalledProductInfo> results)
    {
        try
        {
            using var uninstallKey = baseKey.OpenSubKey(subKeyPath);
            if (uninstallKey is null)
            {
                return;
            }

            foreach (var productKeyName in uninstallKey.GetSubKeyNames())
            {
                using var productKey = uninstallKey.OpenSubKey(productKeyName);
                var displayName = productKey?.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                results.Add(new InstalledProductInfo(
                    displayName,
                    productKey?.GetValue("DisplayVersion") as string,
                    productKey?.GetValue("InstallLocation") as string,
                    productKey?.GetValue("UninstallString") as string));
            }
        }
        catch (Exception)
        {
            // Некоторые ветки реестра могут быть недоступны — пропускаем без прерывания диагностики.
        }
    }
}
