using System.IO;
using System.Text;
using SetupAssistant.Models;

namespace SetupAssistant.Helpers;

/// <summary>Отчёт полной очистки (report.txt в папке отчёта) и предпросмотр «что будет удалено».</summary>
public static class FullCleanupReport
{
    public const string FileName = "report.txt";

    /// <summary>Найденное, по разделам: «== Раздел (N) ==» и строки «[Производитель] запись».</summary>
    public static string FormatItems(IEnumerable<CleanupItem> items, Func<CleanupItem, string?>? status = null)
    {
        var text = new StringBuilder();
        foreach (var group in items.GroupBy(i => i.Category))
        {
            text.AppendLine($"== {group.Key} ({group.Count()}) ==");
            foreach (var item in group)
            {
                var mark = status?.Invoke(item);
                text.AppendLine($"  [{VendorName(item.Vendor)}] {item}" + (item.IsContainerData ? " — ключевые контейнеры" : string.Empty)
                                + (string.IsNullOrEmpty(mark) ? string.Empty : $" — {mark}"));
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    public static string FormatContainers(IReadOnlyList<KeyContainerInfo> containers)
    {
        if (containers.Count == 0)
        {
            return "Ключевых контейнеров на компьютере не найдено." + Environment.NewLine;
        }

        var text = new StringBuilder($"Ключевые контейнеры на компьютере ({containers.Count}):{Environment.NewLine}");
        foreach (var container in containers)
        {
            text.AppendLine($"  {container.Name} — {container.Owner}, {(container.InRegistry ? "реестр" : "папка")}: {container.Location}");
        }

        return text.ToString();
    }

    /// <summary>Расширения в профилях браузеров, которые нужно удалить вручную.</summary>
    public static string FormatBrowserExtensions(IReadOnlyList<BrowserExtensionInfo> extensions)
    {
        if (extensions.Count == 0)
        {
            return "Расширений из списка приложения в профилях браузеров нет." + Environment.NewLine;
        }

        var text = new StringBuilder(
            $"Расширения в профилях браузеров ({extensions.Count}) — удалите вручную: страница chrome://extensions " +
            $"(в Яндекс.Браузере — browser://extensions), кнопка «Удалить». Браузер хранит их в своём профиле, очистка Windows их не трогает.{Environment.NewLine}");
        foreach (var extension in extensions)
        {
            text.AppendLine($"  {extension.Name} — {extension.Browser}, профиль {extension.Profile}, пользователь {extension.Owner} ({extension.Id})");
        }

        return text.ToString();
    }

    /// <summary>Коротко для шага: «Госплагин, Госуслуги (Chromium / Chromium GOST)».</summary>
    public static string SummarizeBrowserExtensions(IReadOnlyList<BrowserExtensionInfo> extensions) =>
        string.Join("; ", extensions.GroupBy(e => e.Browser)
            .Select(g => $"{string.Join(", ", g.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase))} ({g.Key})"));

    /// <summary>Дописывает раздел в report.txt папки отчёта (UTF-8 с BOM, чтобы Блокнот показал кириллицу).</summary>
    public static void Append(string backupDirectory, string title, string body)
    {
        var text = $"===== {DateTime.Now:dd.MM.yyyy HH:mm:ss} · {title} ====={Environment.NewLine}{body.TrimEnd()}{Environment.NewLine}{Environment.NewLine}";
        File.AppendAllText(Path.Combine(backupDirectory, FileName), text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static string VendorName(CryptoVendor vendor) => vendor switch
    {
        CryptoVendor.CryptoPro => "КриптоПро",
        CryptoVendor.Rutoken => "Рутокен",
        CryptoVendor.Kontur => "Контур.Плагин",
        CryptoVendor.Gosuslugi => "Плагин Госуслуг",
        CryptoVendor.SecurityCode => "Код Безопасности",
        _ => vendor.ToString()
    };
}
