using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace SetupAssistant.Helpers;

public readonly record struct CsptestRun(int ExitCode, string Output, bool TimedOut);

/// <summary>Запуск csptest.exe из состава КриптоПро CSP с захватом вывода в кодировке консоли.</summary>
public static class CsptestRunner
{
    public static string? FindPath()
    {
        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Crypto Pro", "CSP", "csptest.exe"))
            .FirstOrDefault(File.Exists);
    }

    public static CsptestRun Run(string csptestPath, string arguments, TimeSpan timeout) =>
        RunAsync(csptestPath, arguments, timeout, CancellationToken.None).GetAwaiter().GetResult();

    public static async Task<CsptestRun> RunAsync(string csptestPath, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var consoleEncoding = GetConsoleEncoding();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = csptestPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = consoleEncoding,
                StandardErrorEncoding = consoleEncoding
            }
        };

        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Процесс уже завершился.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new CsptestRun(-1, (await stdOutTask + await stdErrTask).Trim(), TimedOut: true);
        }

        return new CsptestRun(process.ExitCode, (await stdOutTask + await stdErrTask).Trim(), TimedOut: false);
    }

    private static Encoding GetConsoleEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception)
        {
            return Encoding.UTF8;
        }
    }
}
