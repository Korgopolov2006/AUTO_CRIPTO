using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="ISystemService"/>
public sealed class SystemService : ISystemService
{
    public SystemInfo GetSystemInfo()
    {
        return new SystemInfo
        {
            ComputerName = Environment.MachineName,
            UserName = Environment.UserName,
            WindowsVersion = GetWindowsVersion(),
            WindowsEdition = GetWindowsEdition(),
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            IsAdministrator = IsRunningAsAdministrator(),
            StartedAt = DateTime.Now
        };
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string GetWindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var productName = key?.GetValue("ProductName") as string ?? "Windows";
            var displayVersion = key?.GetValue("DisplayVersion") as string;
            var buildNumber = key?.GetValue("CurrentBuildNumber") as string;

            return string.Join(" ", new[] { productName, displayVersion, buildNumber is null ? null : $"(сборка {buildNumber})" }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        }
        catch (Exception)
        {
            return Environment.OSVersion.VersionString;
        }
    }

    private static string GetWindowsEdition()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue("EditionID") as string ?? "—";
        }
        catch (Exception)
        {
            return "—";
        }
    }
}
