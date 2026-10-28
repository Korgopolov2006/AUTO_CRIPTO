using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using SetupAssistant.Helpers;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Cli;

/// <summary>Выполнение --check и --silent без окна. Печатает ход работы в консоль, из которой запущено приложение.</summary>
public sealed class HeadlessRunner
{
    private const int AttachParentProcess = -1;
    private const int ExitPreflightFailed = 3;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    private readonly IServiceProvider _services;

    public HeadlessRunner(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<int> RunAsync(CommandLineOptions options)
    {
        AttachToParentConsole();

        try
        {
            if (options.Help || options.Error is not null)
            {
                if (options.Error is not null)
                {
                    Print(options.Error);
                    Print(string.Empty);
                }

                Print(CommandLineOptions.Usage);
                return options.Error is null ? 0 : CommandLineOptions.ExitInvalidArguments;
            }

            if (options.SupportBundlePath is not null)
            {
                return await SupportBundleAsync(options.SupportBundlePath);
            }

            if (options.UpdaterArgs is not null)
            {
                // Папка по умолчанию — рядом с exe: так можно обновить распакованное приложение; для единого exe укажите папку с исходниками.
                return await ExtensionUpdater.UpdaterCli.RunAsync(options.UpdaterArgs.ToArray(), AppContext.BaseDirectory, standalone: false);
            }

            if (options.CleanupPreviewPath is not null)
            {
                return CleanupPreview(options.CleanupPreviewPath);
            }

            if (options.Uninstall)
            {
                return await UninstallAsync(options.KontinentOnly, options.Full, options.DeleteContainers);
            }

            return options.Check ? await CheckAsync(options) : await SilentAsync(options);
        }
        catch (Exception ex)
        {
            _services.GetRequiredService<ILoggingService>().Error("Режим без окна", "Непредвиденная ошибка", ex.ToString());
            Print($"Ошибка: {ex.Message}");
            return 2;
        }
    }

    private async Task<int> SupportBundleAsync(string path)
    {
        // Сведения о сертификатах — персональные данные, поэтому без запроса пользователя в пакет не добавляются.
        var created = await _services.GetRequiredService<ISupportBundleService>().CreateAsync(Path.GetFullPath(path), includeCertificates: false);
        Print($"Пакет для поддержки создан: {created}");
        return 0;
    }

    private async Task<int> CheckAsync(CommandLineOptions options)
    {
        var summary = await _services.GetRequiredService<IWorkplaceStatusService>().RefreshAsync();

        Print($"Проверено компонентов: {summary.Results.Count}");
        foreach (var component in summary.Problems)
        {
            Print($"  [{component.StatusText}] {component.Name}" + (string.IsNullOrEmpty(component.ErrorMessage) ? string.Empty : $" — {component.ErrorMessage}"));
        }

        // Не установлено / не подключён / не в домене — сведения: не на каждом компьютере нужен весь набор, на код возврата не влияют.
        foreach (var info in summary.Results.Where(c => c.Status == StatusLevel.Info && (c.ShowOnDashboard || c.IsDeviceManagement)))
        {
            Print($"  [К сведению] {info.Name} — {info.DashboardText}");
        }

        Print(summary.Problems.Count == 0
            ? $"Проблем не найдено. К сведению: {summary.InfoCount}; можно установить мастером: {summary.Missing.Count}."
            : $"Ошибок: {summary.ErrorCount}, предупреждений: {summary.WarningCount}. К сведению: {summary.InfoCount}.");

        ExportReport(options, summary, Array.Empty<string>(), TimeSpan.Zero);
        return summary.ExitCode;
    }

    private void PrintChecks(IEnumerable<PreflightCheck> checks)
    {
        foreach (var check in checks)
        {
            Print($"  [{check.Status}] {check.Name}: {check.Message}");
            if (check.HasAdvice && check.Status != StatusLevel.Ok)
            {
                Print($"      Что сделать: {check.Advice}");
            }
        }
    }

    private async Task<int> SilentAsync(CommandLineOptions options)
    {
        var logger = _services.GetRequiredService<ILoggingService>();
        logger.Info("Тихая установка", "Запуск без окна (--silent)");

        var preflight = await _services.GetRequiredService<IPreflightService>().RunAsync();
        PrintChecks(preflight);

        if (preflight.Any(c => c.IsBlocking))
        {
            Print("Установка невозможна: устраните проблему из списка выше.");
            return ExitPreflightFailed;
        }

        var config = _services.GetRequiredService<IConfigService>();
        var steps = SetupPlan.Build(config, includeTokenStep: options.WithToken);
        var total = steps.Count;
        var finished = 0;

        // Progress<T> не подходит: он отправляет отчёты в поток UI, а здесь важен порядок вывода — печатаем сразу.
        var progress = new SyncProgress<SetupStepModel>(step =>
        {
            if (step.Status is StatusLevel.InProgress or StatusLevel.NotChecked)
            {
                return;
            }

            finished++;
            Print($"[{finished}/{total}] {step.Title}: {step.Status}" + (string.IsNullOrEmpty(step.Details) ? string.Empty : $" — {step.Details}"));
        });

        var started = DateTime.Now;
        await _services.GetRequiredService<ISetupOrchestrationService>()
            .RunSetupAsync(steps, progress, new SetupRunOptions(options.Reinstall));
        var duration = DateTime.Now - started;

        var errors = steps.Count(s => s.Status == StatusLevel.Error);
        var warnings = steps.Count(s => s.Status == StatusLevel.Warning);
        var reboot = steps.Any(s => s.NeedsReboot);

        Print($"Готово за {(int)duration.TotalMinutes:00}:{duration.Seconds:00}. Ошибок: {errors}, предупреждений: {warnings}.");
        if (reboot)
        {
            Print("Для завершения установки требуется перезагрузка компьютера.");
        }

        var failures = steps.Where(s => s.Status is StatusLevel.Error or StatusLevel.Warning).Select(s => $"{s.Title}: {s.Details}").ToList();
        ExportReport(options, _services.GetRequiredService<IWorkplaceStatusService>().Summary, failures, duration);
        return errors > 0 ? 2 : warnings > 0 ? 1 : 0;
    }

    /// <summary>Откат настройки без окна: те же шаги, что на экране «Удаление»; личные сертификаты не удаляются.</summary>
    private int CleanupPreview(string path)
    {
        var full = Path.GetFullPath(path);
        File.WriteAllText(full, _services.GetRequiredService<IUninstallService>().PreviewFullCleanup(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Print($"Предпросмотр полной очистки сохранён: {full}");
        return 0;
    }

    private async Task<int> UninstallAsync(bool kontinentOnly, bool full, bool deleteContainers)
    {
        _services.GetRequiredService<ILoggingService>().Info("Тихое удаление", full ? "Запуск без окна (--uninstall --full)" : "Запуск без окна (--uninstall)");

        var preflight = await _services.GetRequiredService<IPreflightService>().RunForUninstallAsync(full);
        PrintChecks(preflight);

        if (preflight.Any(c => c.IsBlocking))
        {
            Print("Удаление невозможно: устраните проблему из списка выше.");
            return ExitPreflightFailed;
        }

        var service = _services.GetRequiredService<IUninstallService>();
        IReadOnlyList<SetupStepModel> steps = service.BuildPlan(new UninstallOptions(RemovePersonalCertificates: false, FullCleanup: full));
        if (kontinentOnly)
        {
            steps = steps.Where(s => service.IsKontinentStep(s)).ToList();
        }
        var total = steps.Count;
        var finished = 0;

        var progress = new SyncProgress<SetupStepModel>(step =>
        {
            if (step.Status is StatusLevel.InProgress or StatusLevel.NotChecked)
            {
                return;
            }

            finished++;
            Print($"[{finished}/{total}] {step.Title}: {step.Status}" + (string.IsNullOrEmpty(step.Details) ? string.Empty : $" — {step.Details}"));
        });

        var started = DateTime.Now;
        if (full)
        {
            // Сначала точка восстановления и резервная копия; контейнеры удаляются, только если указан --delete-containers и копия удалась.
            var session = service.CreateFullCleanupSession(includeKontinent: true);
            var preparation = steps.Where(s => s.Kind is SetupStepKind.CreateRestorePoint or SetupStepKind.BackupContainers).ToList();
            await service.RunAsync(preparation, progress, session: session);

            session.DeleteContainers = deleteContainers && session.ContainersBackedUp && session.Containers.Count > 0;
            if (session.Containers.Count > 0)
            {
                Print(session.DeleteContainers
                    ? $"Ключевые контейнеры ({session.Containers.Count}) будут удалены: резервная копия сделана."
                    : $"Ключевые контейнеры ({session.Containers.Count}) остаются на компьютере" +
                      (deleteContainers ? ": резервная копия не удалась." : " (удалить: --delete-containers)."));
            }

            await service.RunAsync(steps.Except(preparation).ToList(), progress, session: session);
            Print($"Папка отчёта (резервные копии и report.txt): {session.BackupDirectory}");
            if (session.ContinueAfterReboot)
            {
                Print("Перезагрузите компьютер и запустите команду ещё раз: очистка доделает то, что Windows удаляет при перезагрузке.");
            }
        }
        else
        {
            await service.RunAsync(steps, progress);
        }

        var duration = DateTime.Now - started;

        var errors = steps.Count(s => s.Status == StatusLevel.Error);
        var warnings = steps.Count(s => s.Status == StatusLevel.Warning);

        Print($"Готово за {(int)duration.TotalMinutes:00}:{duration.Seconds:00}. Ошибок: {errors}, предупреждений: {warnings}.");
        if (steps.Any(s => s.NeedsReboot))
        {
            Print("Для завершения удаления требуется перезагрузка компьютера.");
        }

        return errors > 0 ? 2 : warnings > 0 ? 1 : 0;
    }

    private void ExportReport(CommandLineOptions options, ReadinessSummary summary, IReadOnlyList<string> errors, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(options.ReportPath))
        {
            return;
        }

        try
        {
            var report = new ReportModel
            {
                SystemInfo = _services.GetRequiredService<ISystemService>().GetSystemInfo(),
                Components = summary.Results.ToList(),
                Certificates = _services.GetRequiredService<ICertificateService>().GetCertificates().ToList(),
                Errors = errors.Count > 0
                    ? errors.ToList()
                    : summary.Problems.Where(c => c.Status == StatusLevel.Error).Select(c => $"{c.Name}: {c.ErrorMessage}").ToList(),
                ExecutionDuration = duration
            };

            var path = Path.GetFullPath(options.ReportPath);
            var format = path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? ReportFormat.Pdf : ReportFormat.Txt;
            _services.GetRequiredService<IReportService>().Export(report, format, path);
            Print($"Отчёт сохранён: {path}");
        }
        catch (Exception ex)
        {
            Print($"Не удалось сохранить отчёт: {ex.Message}");
        }
    }

    /// <summary>
    /// Приложение оконное, поэтому подключаем консоль родителя (cmd/PowerShell), чтобы вывод был виден. Кодировку не трогаем:
    /// Console сам берёт кодовую страницу этой консоли. Если вывод перенаправлен в файл, консоль не нужна — запись идёт в него.
    /// </summary>
    private static void AttachToParentConsole() => AttachConsole(AttachParentProcess);

    private static void Print(string text) => Console.WriteLine(text);

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SyncProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }
}
