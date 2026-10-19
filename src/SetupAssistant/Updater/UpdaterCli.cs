using System.IO;
using System.Text;

namespace ExtensionUpdater;

/// <summary>
/// Разбор аргументов обновления дистрибутивов и расширений. Общий для режима <c>--update-distributions</c> основного exe
/// и для инструмента сборки ExtensionUpdater (очистка старых .crx перед упаковкой единого exe).
/// </summary>
public static class UpdaterCli
{
    /// <summary>
    /// Аргументы: <c>[папка приложения] [--check] [--prune] [--prune-only]</c>. Возвращает код возврата (0 — всё хорошо).
    /// </summary>
    /// <param name="defaultAppDir">Папка приложения, если путь не указан аргументом.</param>
    /// <param name="standalone">Отдельный запуск двойным щелчком: утилита задаёт кодировку UTF-8, спрашивает про очистку и ждёт Enter.</param>
    public static async Task<int> RunAsync(string[] args, string defaultAppDir, bool standalone)
    {
        if (standalone)
        {
            Console.OutputEncoding = Encoding.UTF8;
        }

        var checkOnly = args.Contains("--check", StringComparer.OrdinalIgnoreCase);
        var pruneOnly = args.Contains("--prune-only", StringComparer.OrdinalIgnoreCase);
        var prune = pruneOnly || args.Contains("--prune", StringComparer.OrdinalIgnoreCase);
        var appDir = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? defaultAppDir);
        var interactive = standalone && args.Length == 0 && !Console.IsInputRedirected;

        var exitCode = await Updater.RunAsync(appDir, checkOnly, prune, askToPrune: interactive, pruneOnly);

        if (interactive)
        {
            Console.WriteLine();
            Console.Write("Нажмите Enter для выхода...");
            Console.ReadLine();
        }

        return exitCode;
    }
}
