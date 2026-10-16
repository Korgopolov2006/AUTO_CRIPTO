using System.Text.RegularExpressions;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IVersionService"/>
public sealed partial class VersionService : IVersionService
{
    public int Compare(string? versionA, string? versionB)
    {
        var a = ParseVersion(versionA);
        var b = ParseVersion(versionB);

        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        return a.CompareTo(b);
    }

    public StatusLevel EvaluateStatus(string? installedVersion, string? minVersion, string? recommendedVersion)
    {
        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            return StatusLevel.Error;
        }

        if (!string.IsNullOrWhiteSpace(minVersion) && Compare(installedVersion, minVersion) < 0)
        {
            return StatusLevel.Error;
        }

        if (!string.IsNullOrWhiteSpace(recommendedVersion) && Compare(installedVersion, recommendedVersion) < 0)
        {
            return StatusLevel.Warning;
        }

        return StatusLevel.Ok;
    }

    private static Version? ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var match = VersionPattern().Match(raw);
        if (!match.Success)
        {
            return null;
        }

        var numericParts = match.Value.Split('.').Select(part => int.TryParse(part, out var n) ? n : 0).ToArray();
        Array.Resize(ref numericParts, 4);

        return new Version(numericParts[0], numericParts[1], Math.Max(numericParts[2], 0), Math.Max(numericParts[3], 0));
    }

    [GeneratedRegex(@"\d+(\.\d+){1,3}")]
    private static partial Regex VersionPattern();
}
