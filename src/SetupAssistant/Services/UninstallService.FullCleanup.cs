using System.IO;
using System.Text;
using SetupAssistant.Helpers;
using SetupAssistant.Models;

namespace SetupAssistant.Services;

/// <summary>
/// Полная очистка: точка восстановления, резервная копия ключевых контейнеров, глубокая очистка следов СКЗИ после удаления программ
/// и её повтор после перезагрузки. Порядок и правила — раздел 7 docs/crypto-cleanup-research.md.
/// </summary>
public sealed partial class UninstallService
{
    private const string FullCleanupAction = "Полная очистка";
    private const string CryptoProCspName = "КриптоПро CSP";

    public FullCleanupSession CreateFullCleanupSession(bool includeKontinent)
    {
        var logs = Path.GetDirectoryName(_logger.CurrentLogFilePath) ?? Path.GetTempPath();
        return new FullCleanupSession
        {
            BackupDirectory = Path.Combine(logs, $"FullCleanup-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"),
            IncludeKontinent = includeKontinent
        };
    }

    public IReadOnlyList<SetupStepModel> BuildResumePlan() => new[] { DeepCleanupStep() };

    public string PreviewFullCleanup()
    {
        var items = CryptoTraceScanner.Scan(CryptoVendor.All, includeContainers: true);
        var installed = _configService.Current.Uninstall.Products
            .Select(p => (Definition: p, Product: FindProduct(p)))
            .Where(p => p.Product is not null)
            .Select(p => $"  {p.Product!.DisplayName}{(string.IsNullOrWhiteSpace(p.Product.DisplayVersion) ? string.Empty : " " + p.Product.DisplayVersion)}")
            .ToList();
        var foreign = CryptoTraceScanner.FindForeignProducts(CryptoVendor.All, IsManagedProduct);

        var text = new StringBuilder();
        text.AppendLine($"Предпросмотр полной очистки СКЗИ, {DateTime.Now:dd.MM.yyyy HH:mm}, компьютер {Environment.MachineName}. Ничего не изменено.");
        text.AppendLine("Список включает и то, что уберёт штатное удаление программ: глубокая очистка ищет следы уже после него.");
        text.AppendLine();
        text.AppendLine(installed.Count == 0 ? "Программ из списка удаления не установлено." : "Будут удалены штатно:");
        installed.ForEach(line => text.AppendLine(line));
        text.AppendLine();
        if (foreign.Count > 0)
        {
            text.AppendLine("Установлены другие программы этих производителей — пока они есть, глубокая очистка не выполняется:");
            foreach (var name in foreign)
            {
                text.AppendLine($"  {name}");
            }

            text.AppendLine();
        }

        text.AppendLine(FullCleanupReport.FormatContainers(CryptoTraceScanner.FindContainers()));
        text.AppendLine(FullCleanupReport.FormatBrowserExtensions(FindProfileExtensions()));
        text.AppendLine($"Найдено следов: {items.Count}");
        text.AppendLine();
        text.Append(FullCleanupReport.FormatItems(items));
        return text.ToString();
    }

    private SetupStepModel DeepCleanupStep()
    {
        // Чужие программы тех же производителей видно заранее: без их удаления глубокая очистка не выполнится.
        var foreign = CryptoTraceScanner.FindForeignProducts(CryptoVendor.All, IsManagedProduct);
        return new SetupStepModel
        {
            Kind = SetupStepKind.DeepCleanup,
            Title = "Глубокая очистка следов СКЗИ",
            Details = foreign.Count == 0
                ? "После удаления программ: криптопровайдеры и OID, CNG и TLS, пакеты LSA, службы и драйверы, COM, записи браузеров, папки и файлы"
                : $"Не выполнится, пока установлены другие программы тех же производителей: {string.Join(", ", foreign)}. " +
                  "Удалите их в «Параметрах Windows → Приложения»"
        };
    }

    /// <summary>Шаги полной очистки, которые идут до удаления программ.</summary>
    private IEnumerable<SetupStepModel> FullCleanupPreparationSteps()
    {
        yield return new SetupStepModel
        {
            Kind = SetupStepKind.CreateRestorePoint,
            Title = "Точка восстановления Windows",
            Details = "Перед удалением; изменения реестра дополнительно сохраняются в папке отчёта"
        };

        var containers = CryptoTraceScanner.FindContainers();
        yield return new SetupStepModel
        {
            Kind = SetupStepKind.BackupContainers,
            Title = "Резервная копия ключевых контейнеров",
            Details = containers.Count == 0
                ? "Ключевых контейнеров на компьютере не найдено. Сертификаты КриптоПро будут сохранены в папку отчёта"
                : $"Найдено контейнеров: {containers.Count} (в реестре: {containers.Count(c => c.InRegistry)}, в папках: {containers.Count(c => !c.InRegistry)}). " +
                  "Удалять ли их с компьютера — приложение спросит после копирования"
        };
    }

    /// <summary>
    /// Расширения в профилях браузеров: из списка приложения (Config.json → Extensions, ID и ID в магазине) и те, что прописывают
    /// установщики плагинов.
    /// </summary>
    private IReadOnlyList<BrowserExtensionInfo> FindProfileExtensions()
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _configService.Current.Extensions.Items)
        {
            foreach (var id in new[] { item.Id, item.StoreId }.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                known.TryAdd(id, item.Name);
            }
        }

        foreach (var id in CryptoTraceScanner.VendorExtensionIds)
        {
            known.TryAdd(id, id);
        }

        return CryptoTraceScanner.FindProfileExtensions(known);
    }

    private bool IsManagedProduct(string displayName)
    {
        static string Normalize(string value) => value.Replace(" ", string.Empty).Replace("-", string.Empty);
        return _configService.Current.Uninstall.Products
            .SelectMany(p => p.DisplayNamePatterns)
            .Any(pattern => Normalize(displayName).Contains(Normalize(pattern), StringComparison.OrdinalIgnoreCase));
    }

    // ---------- Шаги ----------

    private async Task CreateRestorePointAsync(SetupStepModel step)
    {
        step.Details = "Создаётся точка восстановления (может занять до минуты)…";
        var (created, message) = await Task.Run(() => RestorePoint.Create("Перед полной очисткой СКЗИ"));
        step.Status = created ? StatusLevel.Ok : StatusLevel.Warning;
        step.Details = created ? message : $"{message}. Удаляемые разделы реестра всё равно сохраняются в папке отчёта";
        if (created)
        {
            _logger.Info(FullCleanupAction, message);
        }
        else
        {
            _logger.Warning(FullCleanupAction, "Точка восстановления не создана", message);
        }
    }

    private async Task BackupContainersAsync(SetupStepModel step, FullCleanupSession session)
    {
        step.Details = "Сохраняются ключевые контейнеры и сертификаты…";
        var problems = new List<string>();
        var certificates = 0;
        string? serial = null;

        await Task.Run(() =>
        {
            KeyContainerBackup.PrepareDirectory(session.BackupDirectory);
            KeyContainerBackup.WriteReadme(session.BackupDirectory);

            session.Containers.Clear();
            session.Containers.AddRange(CryptoTraceScanner.FindContainers());
            session.ContainersBackedUp = KeyContainerBackup.BackupContainers(session.Containers, session.BackupDirectory, problems);
            certificates = KeyContainerBackup.ExportCertificates(session.BackupDirectory);

            var csp = _configService.Current.Uninstall.Products.FirstOrDefault(p => p.Name == CryptoProCspName);
            var product = csp is null ? null : FindProduct(csp);
            serial = KeyContainerBackup.ReadCryptoProSerial(ExtractProductCode(product?.KeyName) ?? ExtractProductCode(product?.UninstallString));

            var header = new StringBuilder();
            header.AppendLine($"Компьютер: {Environment.MachineName}, пользователь: {Environment.UserDomainName}\\{Environment.UserName}");
            header.AppendLine($"Затрагиваются: КриптоПро, Рутокен, Контур.Плагин, Плагин Госуслуг{(session.IncludeKontinent ? ", Континент-АП и Код Безопасности CSP" : string.Empty)}");
            header.AppendLine(serial is null
                ? "Серийный номер КриптоПро CSP в системе не найден (не введён или программа не установлена)."
                : $"Серийный номер КриптоПро CSP: {serial} — понадобится при повторной установке.");
            header.AppendLine($"Сертификатов КриптоПро сохранено: {certificates} (папка certificates).");
            header.AppendLine();
            header.Append(FullCleanupReport.FormatContainers(session.Containers));
            if (problems.Count > 0)
            {
                header.AppendLine("Не удалось сохранить:");
                problems.ForEach(p => header.AppendLine($"  {p}"));
            }

            FullCleanupReport.Append(session.BackupDirectory, "Резервная копия перед полной очисткой", header.ToString());
        });

        if (problems.Count > 0)
        {
            step.Status = StatusLevel.Error;
            step.Details = $"Копия сделана не полностью — контейнеры останутся на компьютере. {string.Join("; ", problems)}";
            _logger.Error(FullCleanupAction, "Резервная копия контейнеров не полная", string.Join("; ", problems));
            return;
        }

        step.Status = StatusLevel.Ok;
        step.Details = (session.Containers.Count == 0 ? "Контейнеров не найдено" : $"Сохранено контейнеров: {session.Containers.Count}")
                       + $", сертификатов: {certificates}. Папка отчёта: {session.BackupDirectory}";
        _logger.Info(FullCleanupAction, $"Резервная копия: контейнеров {session.Containers.Count}, сертификатов {certificates}, папка {session.BackupDirectory}");
    }

    private async Task DeepCleanupAsync(SetupStepModel step, IReadOnlyList<SetupStepModel> steps, FullCleanupSession session)
    {
        // Удалять следы программы, которая ещё установлена, нельзя: это сломало бы её, а не удалило.
        if (steps.Any(s => s.Kind == SetupStepKind.UninstallProduct && s.Status == StatusLevel.Error))
        {
            step.Status = StatusLevel.Warning;
            step.Details = "Пропущено: не все программы удалены. Повторите удаление, затем полную очистку";
            _logger.Warning(FullCleanupAction, "Глубокая очистка пропущена: есть неудалённые программы");
            return;
        }

        var stillInstalled = _configService.Current.Uninstall.Products
            .Where(p => session.IncludeKontinent || p.Group != "Kontinent")
            .Where(p => FindProduct(p) is not null)
            .Select(p => p.Name)
            .ToList();
        if (stillInstalled.Count > 0 || WindowsInstallerState.IsBusy())
        {
            var reason = stillInstalled.Count > 0 ? $"ещё числятся установленными: {string.Join(", ", stillInstalled)}" : "Windows Installer ещё работает";
            if (session.IsResume)
            {
                step.Status = StatusLevel.Error;
                step.Details = $"Не выполнено: программы {reason}. Удалите их (кнопка «Удалить» с флажком «Полная очистка»)";
            }
            else
            {
                step.Status = StatusLevel.Warning;
                step.NeedsReboot = true;
                step.Details = $"Будет выполнена после перезагрузки: Windows завершит удаление (программы {reason})";
                session.ContinueAfterReboot = true;
            }

            _logger.Warning(FullCleanupAction, "Глубокая очистка отложена", reason);
            return;
        }

        var foreign = CryptoTraceScanner.FindForeignProducts(session.Vendors, IsManagedProduct);
        if (foreign.Count > 0)
        {
            step.Status = StatusLevel.Error;
            step.Details = $"Не выполнено: установлены другие программы тех же производителей ({string.Join(", ", foreign)}). " +
                           "Удалите их в «Параметрах Windows → Приложения» и запустите полную очистку снова";
            _logger.Warning(FullCleanupAction, "Глубокая очистка не выполнена: есть другие программы производителей", string.Join(", ", foreign));
            return;
        }

        step.Details = "Поиск следов…";
        var outcome = await Task.Run(() =>
        {
            Directory.CreateDirectory(Path.Combine(session.BackupDirectory, "registry"));
            var items = CryptoTraceScanner.Scan(session.Vendors, includeContainers: session.DeleteContainers);
            var backupFile = Path.Combine(session.BackupDirectory, "registry", $"removed-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.reg");
            var result = CryptoTraceRemover.Remove(items, backupFile, session.DeleteContainers, (action, details) => _logger.Warning(FullCleanupAction, action, details));

            // Проверка: повторный поиск. Остаться может только то, что назначено на удаление при перезагрузке.
            var scheduled = result.Scheduled.Select(i => i.ToString()).ToHashSet();
            var remaining = CryptoTraceScanner.Scan(session.Vendors, includeContainers: session.DeleteContainers)
                .Where(i => !scheduled.Contains(i.ToString()) && i.Kind != CleanupItemKind.Service)
                .ToList();

            var failed = result.Failed.ToDictionary(f => f.Item, f => f.Error);
            var report = new StringBuilder();
            report.AppendLine($"Найдено: {items.Count}; удалено: {result.Removed.Count}; удалится при перезагрузке: {result.Scheduled.Count}; не удалось: {result.Failed.Count}.");
            report.AppendLine(session.DeleteContainers
                ? "Ключевые контейнеры удалялись (было подтверждено после резервной копии)."
                : "Ключевые контейнеры не удалялись.");
            report.AppendLine($"Разделы реестра до удаления: {backupFile}");
            report.AppendLine();
            report.Append(FullCleanupReport.FormatItems(items, item =>
                failed.TryGetValue(item, out var error) ? $"НЕ УДАЛЕНО: {error}"
                : result.Scheduled.Contains(item) ? "при перезагрузке"
                : null));
            if (remaining.Count > 0)
            {
                report.AppendLine("Осталось после очистки (повторный поиск):");
                report.Append(FullCleanupReport.FormatItems(remaining));
            }

            var extensions = FindProfileExtensions();
            report.AppendLine();
            report.Append(FullCleanupReport.FormatBrowserExtensions(extensions));

            FullCleanupReport.Append(session.BackupDirectory, session.IsResume ? "Глубокая очистка после перезагрузки" : "Глубокая очистка", report.ToString());

            // Неудавшиеся записи повторный поиск находит снова — не считаем их дважды.
            var failedNames = result.Failed.Select(f => f.Item.ToString()).ToHashSet();
            return (Result: result, Remaining: remaining.Count(i => !failedNames.Contains(i.ToString())), Extensions: extensions);
        });

        var (cleanup, left, extensions) = outcome;
        step.NeedsReboot = cleanup.Scheduled.Count > 0;
        var summary = $"Удалено записей и файлов: {cleanup.Removed.Count}"
                      + (cleanup.Scheduled.Count > 0 ? $", при перезагрузке удалится ещё {cleanup.Scheduled.Count}" : string.Empty);
        if (cleanup.Failed.Count > 0 || left > 0)
        {
            step.Status = StatusLevel.Warning;
            step.Details = $"{summary}. Не удалось: {cleanup.Failed.Count + left} — подробности в report.txt папки отчёта";
        }
        else
        {
            step.Status = StatusLevel.Ok;
            step.Details = summary;
        }

        // Расширения в профилях браузеров остаются: их удаляет только сам браузер, поэтому шаг подсказывает, что сделать.
        if (extensions.Count > 0)
        {
            step.Status = StatusLevel.Warning;
            step.Details += $". Удалите вручную в браузере: {FullCleanupReport.SummarizeBrowserExtensions(extensions)}";
        }

        _logger.Info(FullCleanupAction, $"{step.Details}. Отчёт: {Path.Combine(session.BackupDirectory, FullCleanupReport.FileName)}");

        // Первый проход до перезагрузки: после неё приложение проверит и доделает (занятые файлы, драйвер, отложенное Windows).
        if (!session.IsResume && (step.NeedsReboot || steps.Any(s => s.NeedsReboot)))
        {
            session.ContinueAfterReboot = true;
        }
    }
}
