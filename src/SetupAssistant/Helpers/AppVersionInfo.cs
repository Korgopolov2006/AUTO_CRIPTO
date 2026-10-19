using System.Reflection;

namespace SetupAssistant.Helpers;

/// <summary>
/// Версия приложения берётся из сборки (<c>Version</c> в SetupAssistant.csproj), а не из конфигурации: так номер в окне, журнале
/// и пакете поддержки всегда совпадает с реальной сборкой. SDK добавляет к версии хеш коммита (1.2.0+abcdef…) — показываем его
/// в сокращённом виде, по нему видно, из какого состояния репозитория собран exe.
/// </summary>
public static class AppVersionInfo
{
    private const int ShortHashLength = 7;

    /// <summary>Например, «1.2.0 (db3480b)»; без хеша (сборка не из git) — просто «1.2.0».</summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        var assembly = typeof(AppVersionInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return assembly.GetName().Version?.ToString(3) ?? "неизвестна";
        }

        var plus = informational.IndexOf('+');
        if (plus < 0)
        {
            return informational;
        }

        var hash = informational[(plus + 1)..];
        return $"{informational[..plus]} ({hash[..Math.Min(ShortHashLength, hash.Length)]})";
    }
}
