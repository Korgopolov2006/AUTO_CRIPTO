using System.IO;
using System.Text;

namespace SetupAssistant.Helpers;

/// <summary>
/// Зависание пакета КриптоПро CSP на запуске или остановке своего драйвера CProCtrl. Действие пакета KSInstInstall/KSInstUninstall
/// ждёт диспетчер служб, а тот — ядро, которое так и не загружает (не выгружает) драйвер: отпускает только перезагрузка.
/// При загрузке Windows драйвер стартует нормально, поэтому после перезагрузки операция завершается.
/// </summary>
public static class CryptoProDriverHang
{
    /// <summary>Обычно действие с драйвером занимает доли секунды: столько тишины в журнале на нём — уже зависание.</summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromMinutes(3);

    public const string Message =
        "КриптоПро CSP завис на запуске или остановке своего драйвера (CProCtrl): Windows отпустит его только при перезагрузке, ждать дальше бесполезно. " +
        "Перезагрузите компьютер — операция завершится, после этого повторите шаг.";

    /// <summary>Текст для случая, когда зависшая раньше операция КриптоПро держит установщик Windows.</summary>
    public static string BusyMessage(DateTime since) =>
        $"Установщик Windows занят операцией КриптоПро CSP, которая зависла в {since:HH:mm} на драйвере CProCtrl и завершится только при перезагрузке. " +
        "Перезагрузите компьютер и повторите.";

    /// <summary>Последняя строка журнала msiexec — вывод действия КриптоПро с драйвером (строки вида «KSInstall64:info: …»).</summary>
    public static bool EndsInDriverAction(string logPath)
    {
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            const int tailBytes = 8192;
            var start = Math.Max(0, stream.Length - tailBytes) & ~1L; // журнал в UTF-16: читаем с чётного байта
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.Unicode, detectEncodingFromByteOrderMarks: false);
            var lastLine = reader.ReadToEnd()
                .Split('\n')
                .Select(line => line.Trim().TrimStart('﻿'))
                .LastOrDefault(line => line.Length > 0);

            // «Install driver CProCtrl success.» — действие уже закончилось, дальше просто не дописан буфер журнала.
            return lastLine is not null
                   && lastLine.StartsWith("KSInstall", StringComparison.OrdinalIgnoreCase)
                   && !lastLine.Contains("Install driver", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Самый свежий журнал msiexec в папке, если он остановился на действии с драйвером дольше <see cref="Threshold"/>; иначе null.
    /// Нужен, когда Windows Installer занят: так видно, что его держит зависшая установка или удаление КриптоПро.
    /// </summary>
    public static FileInfo? FindStuckLog(string logDirectory)
    {
        try
        {
            var latest = new DirectoryInfo(logDirectory).EnumerateFiles("msi-*.log")
                .OrderByDescending(file => file.LastWriteTime)
                .FirstOrDefault();

            return latest is not null && DateTime.Now - latest.LastWriteTime >= Threshold && EndsInDriverAction(latest.FullName)
                ? latest
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
