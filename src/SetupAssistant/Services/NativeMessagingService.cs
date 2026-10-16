using System.IO;
using Microsoft.Win32;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="INativeMessagingService"/>
public sealed class NativeMessagingService : INativeMessagingService
{
    private const string CadesComProgId = "CAdESCOM.CPSigner";

    private static readonly RegistryView[] Views = { RegistryView.Registry64, RegistryView.Registry32 };

    private readonly IConfigService _configService;

    public NativeMessagingService(IConfigService configService)
    {
        _configService = configService;
    }

    public IReadOnlyList<ComponentInfo> CheckHosts() =>
        _configService.Current.NativeMessaging.Hosts.Select(CheckHost).ToList();

    public ComponentInfo CheckCadesCom()
    {
        var registered = Views.Any(view =>
        {
            using var classesRoot = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
            using var key = classesRoot.OpenSubKey(CadesComProgId);
            return key is not null;
        });

        return new ComponentInfo
        {
            Type = ComponentType.CadesCom,
            Name = "COM-компонент CAdESCOM",
            Description = "Компонент КриптоПро ЭЦП Browser plug-in для подписи через COM",
            Status = registered ? StatusLevel.Ok : StatusLevel.Error,
            ErrorMessage = registered ? null : "CAdESCOM не зарегистрирован: установите КриптоПро ЭЦП Browser plug-in"
        };
    }

    private ComponentInfo CheckHost(NativeHostDefinition host)
    {
        var registeredIn = new HashSet<string>();
        var brokenIn = new HashSet<string>();

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in Views)
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var root in _configService.Current.NativeMessaging.RegistryRoots)
                {
                    using var key = baseKey.OpenSubKey($@"{root}\{host.Name}");
                    if (key is null)
                    {
                        continue;
                    }

                    var location = $@"{(hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU")}\{root}";
                    var manifestPath = key.GetValue(null) as string;
                    var manifestExists = !string.IsNullOrWhiteSpace(manifestPath)
                                         && File.Exists(Environment.ExpandEnvironmentVariables(manifestPath));

                    (manifestExists ? registeredIn : brokenIn).Add(location);
                }
            }
        }

        var found = registeredIn.Count > 0;
        var hint = string.IsNullOrWhiteSpace(host.Description) ? "плагин" : host.Description;

        return new ComponentInfo
        {
            Type = ComponentType.NativeMessagingHost,
            Name = $"Native messaging: {host.Name}",
            Description = found
                ? $"Хост зарегистрирован: {string.Join("; ", registeredIn)}"
                : $"Хост для расширений браузера ({hint})",
            Status = found ? StatusLevel.Ok : StatusLevel.Error,
            ErrorMessage = found
                ? null
                : brokenIn.Count > 0
                    ? $"Хост зарегистрирован ({string.Join("; ", brokenIn)}), но файл манифеста не найден: переустановите {hint}"
                    : $"Хост не зарегистрирован: установите или переустановите {hint}"
        };
    }
}
