using System.Text;
using ExtensionUpdater;

Console.OutputEncoding = Encoding.UTF8;

var checkOnly = args.Contains("--check", StringComparer.OrdinalIgnoreCase);
var prune = args.Contains("--prune", StringComparer.OrdinalIgnoreCase);
var appDir = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? AppContext.BaseDirectory);
var interactive = args.Length == 0 && !Console.IsInputRedirected;
var pauseAtEnd = interactive;

var exitCode = await Updater.RunAsync(appDir, checkOnly, prune, askToPrune: interactive);

if (pauseAtEnd)
{
    Console.WriteLine();
    Console.Write("Нажмите Enter для выхода...");
    Console.ReadLine();
}

return exitCode;
