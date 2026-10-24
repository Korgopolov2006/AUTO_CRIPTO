using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SetupAssistant.Helpers;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IUninstallService"/>
/// <remarks>
/// Ключи, ключевые контейнеры и содержимое токена не удаляются никогда: это необратимая потеря электронной подписи.
/// Ветки реестра КриптоПро с пользовательскими контейнерами в список очистки не входят.
/// </remarks>
public sealed partial class UninstallService : IUninstallService
{
    private const string Action = "Удаление";
    private const string OurUpdatesManifest = "SetupAssistant/Extensions/updates.xml";

    private static readonly Regex ProductCodePattern = ProductCodeRegex();

    // Ветка, которую записывали ранние версии приложения; Chromium GOST её не читает, но запись надо убрать.
    private const string LegacyPolicyKey = @"SOFTWARE\Policies\ChromiumGost\ExtensionInstallForcelist";

    private readonly IConfigService _configService;
    private readonly IRegistryService _registryService;
    private readonly IInstallerService _installerService;
    private readonly ILoggingService _logger;

    public UninstallService(
        IConfigService configService,
        IRegistryService registryService,
        IInstallerService installerService,
        ILoggingService logger)
    {
        _configService = configService;
        _registryService = registryService;
        _installerService = installerService;
        _logger = logger;
    }

    public IReadOnlyList<SetupStepModel> BuildPlan(UninstallOptions options)
    {
        var steps = new List<SetupStepModel>();

        foreach (var product in _configService.Current.Uninstall.Products)
        {
            var found = FindProduct(product);
            steps.Add(new SetupStepModel
            {
                Kind = SetupStepKind.UninstallProduct,
                Key = product.Name,
                Title = $"Удаление: {product.Name}",
                Details = found is null
                    ? "Не установлено — будет пропущено"
                    : $"Найдено: {found.DisplayName}{(string.IsNullOrWhiteSpace(found.DisplayVersion) ? string.Empty : " " + found.DisplayVersion)}"
            });
        }

        steps.Add(new SetupStepModel
        {
            Kind = SetupStepKind.RemoveExtensionPolicy,
            Title = "Удаление политики установки расширений браузера",
            Details = $"Записей, добавленных приложением: {FindPolicyEntries().Count}"
        });

        if (options.RemovePersonalCertificates)
        {
            steps.Add(new SetupStepModel
            {
                Kind = SetupStepKind.RemovePersonalCertificates,
                Title = "Удаление сертификатов КриптоПро из хранилища «Личное»",
                Details = $"Сертификатов, привязанных к ключам КриптоПро: {FindCryptoProCertificates().Count} (ключи и токен не затрагиваются)"
            });
        }

        steps.Add(new SetupStepModel
        {
            Kind = SetupStepKind.RemoveLeftovers,
            Title = "Очистка остаточных файлов и данных",
            Details = $"Найдено для очистки: папок {ResolveLeftoverDirectories().Count + ResolveEmptyFolders().Count}, " +
                      $"веток реестра {ResolveLeftoverRegistryKeys().Count + ResolveEmptyRegistryBranches().Count + ResolveOrphanProgIds().Count}"
        });

        steps.Add(new SetupStepModel
        {
            Kind = SetupStepKind.RemoveAppCache,
            Title = "Очистка кэша приложения",
            Details = "Распакованные дистрибутивы и скачанные файлы"
        });

        return steps;
    }

    public bool IsKontinentStep(SetupStepModel step) =>
        step.Kind == SetupStepKind.UninstallProduct
        && _configService.Current.Uninstall.Products.Any(p => p.Name == step.Key && p.Group == "Kontinent");

    public async Task RunAsync(IReadOnlyList<SetupStepModel> steps, IProgress<SetupStepModel>? progress, CancellationToken cancellationToken = default)
    {
        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            step.IsActive = true;
            step.Status = StatusLevel.InProgress;
            progress?.Report(step);

            try
            {
                switch (step.Kind)
                {
                    case SetupStepKind.UninstallProduct:
                        await UninstallProductAsync(step, cancellationToken);
                        break;
                    case SetupStepKind.RemoveExtensionPolicy:
                        RemoveExtensionPolicy(step);
                        break;
                    case SetupStepKind.RemovePersonalCertificates:
                        RemovePersonalCertificates(step);
                        break;
                    case SetupStepKind.RemoveLeftovers:
                        if (steps.Any(s => s.Kind == SetupStepKind.UninstallProduct && s.Status == StatusLevel.Error))
                        {
                            step.Status = StatusLevel.Warning;
                            step.Details = "Пропущено: не все компоненты удалены, чистка данных сломала бы то, что ещё установлено. Повторите удаление.";
                            _logger.Warning(Action, "Очистка остатков пропущена: есть неудалённые компоненты");
                        }
                        else
                        {
                            RemoveLeftovers(step);
                        }

                        break;
                    case SetupStepKind.RemoveAppCache:
                        RemoveAppCache(step);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                step.Status = StatusLevel.Error;
                step.Details = "Удаление отменено пользователем";
                progress?.Report(step);
                throw;
            }
            catch (Exception ex)
            {
                step.Status = StatusLevel.Error;
                step.Details = ex.Message;
                _logger.Error($"{Action}: {step.Title}", "Ошибка выполнения шага", ex.Message);
            }
            finally
            {
                step.IsActive = false;
                progress?.Report(step);
            }
        }
    }

    public async Task<RollbackResult> RollbackProductsAsync(IReadOnlyCollection<string> productNames, bool cleanCryptoProRemains, IProgress<string>? status, CancellationToken cancellationToken = default)
    {
        const string rollback = "Откат установки";
        var removed = 0;
        var failed = 0;
        var needsReboot = false;

        // Порядок из конфигурации обратен порядку установки (сначала зависимые компоненты, последним КриптоПро CSP).
        foreach (var definition in _configService.Current.Uninstall.Products.Where(p => productNames.Contains(p.Name)))
        {
            if (FindProduct(definition) is null)
            {
                continue;
            }

            var step = new SetupStepModel { Kind = SetupStepKind.UninstallProduct, Key = definition.Name, Title = $"Откат: {definition.Name}" };
            status?.Report($"Откат: удаляется {definition.Name}…");
            step.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SetupStepModel.Details) && !string.IsNullOrEmpty(step.Details))
                {
                    status?.Report($"Откат: {definition.Name}. {step.Details}");
                }
            };

            try
            {
                await UninstallProductAsync(step, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                step.Status = StatusLevel.Error;
                _logger.Error($"{rollback}: {definition.Name}", "Ошибка отката", ex.Message);
            }

            needsReboot |= step.NeedsReboot;
            if (step.Status == StatusLevel.Ok)
            {
                removed++;
            }
            else
            {
                failed++;
                _logger.Warning($"{rollback}: {definition.Name}", "Продукт не удалось откатить", step.Details);
            }
        }

        if (cleanCryptoProRemains && failed == 0)
        {
            status?.Report("Откат: очистка остатков КриптоПро…");
            var cleanup = new SetupStepModel { Kind = SetupStepKind.RemoveLeftovers, Title = "Откат: остатки КриптоПро" };
            RemoveLeftovers(cleanup, cryptoProOnly: true);
            _logger.Info(rollback, cleanup.Details ?? string.Empty);
            needsReboot |= RemoveStaleDriver("CProCtrl");
        }

        TryDeleteDownloads();
        return new RollbackResult(removed, failed, needsReboot);
    }

    /// <summary>Службу-драйвер, оставшуюся от прерванной установки, останавливает и удаляет. Истина — драйвер удалится только после перезагрузки.</summary>
    private bool RemoveStaleDriver(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            if (key is null)
            {
                return false;
            }

            // Драйвер нужен только работающему КриптоПро: если его запись в списке программ ещё есть, он не «остаточный».
            if (_configService.Current.Uninstall.Products.Any(p => p.Name == "КриптоПро CSP" && FindProduct(p) is not null))
            {
                return false;
            }

            RunSc($"stop {serviceName}");
            var code = RunSc($"delete {serviceName}");
            _logger.Info("Откат установки", $"Драйвер {serviceName} удалён из системы (код sc: {code})");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning("Откат установки", $"Не удалось удалить драйвер {serviceName}", ex.Message);
            return false;
        }
    }

    private static int RunSc(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("sc.exe", arguments) { UseShellExecute = false, CreateNoWindow = true })
                            ?? throw new InvalidOperationException("sc.exe не запущен");
        return process.WaitForExit(15_000) ? process.ExitCode : -1;
    }

    private void TryDeleteDownloads()
    {
        try
        {
            var downloads = Path.Combine(Path.GetTempPath(), "SetupAssistant", "Downloads");
            if (Directory.Exists(downloads))
            {
                Directory.Delete(downloads, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("Откат установки", "Не удалось удалить скачанные файлы", ex.Message);
        }
    }

    // ---------- Продукты ----------

    private InstalledProductInfo? FindProduct(UninstallProductDefinition definition) =>
        definition.DisplayNamePatterns
            .Select(_registryService.FindInstalledProduct)
            .FirstOrDefault(p => p is not null);

    private async Task UninstallProductAsync(SetupStepModel step, CancellationToken cancellationToken)
    {
        var definition = _configService.Current.Uninstall.Products.First(p => p.Name == step.Key);
        var product = FindProduct(definition);
        if (product is null)
        {
            // Саму программу уже удалили, но её настройки могли остаться: убираем и их.
            var leftoverSettings = RemoveProductData(definition);
            step.Status = StatusLevel.Ok;
            step.Details = leftoverSettings > 0 ? $"Не установлено. Удалены оставшиеся настройки программы (папок: {leftoverSettings})" : "Не установлено";
            return;
        }

        var (file, arguments) = BuildCommand(product, definition);
        if (file is null)
        {
            step.Status = StatusLevel.Error;
            step.Details = "Не удалось определить команду удаления (нет кода продукта или пути деинсталлятора)";
            _logger.Error($"{Action}: {definition.Name}", "Команда удаления не найдена", product.UninstallString);
            return;
        }

        // Запись о программе есть, а деинсталлятора нет (папку убрали или программа обновилась в другую папку версии).
        if (definition.Kind == InstallerKind.Exe && Path.IsPathRooted(file) && !File.Exists(file))
        {
            var alternative = FindSiblingUninstaller(file);
            if (alternative is not null)
            {
                _logger.Warning($"{Action}: {definition.Name}", $"Деинсталлятор из записи не найден ({file}), используется {alternative}");
                file = alternative;
            }
            else
            {
                // Удалять нечего: программа уже удалена. Убираем только мёртвую запись, иначе она будет мешать при каждом удалении.
                var removed = _registryService.RemoveUninstallEntry(product);
                step.Status = removed ? StatusLevel.Ok : StatusLevel.Warning;
                step.Details = removed
                    ? "Файлов программы уже нет — убрана оставшаяся запись в списке программ"
                    : "Файлов программы уже нет, но запись в списке программ удалить не удалось";
                _logger.Warning($"{Action}: {definition.Name}", $"Деинсталлятор не найден ({file}): программа уже удалена. " +
                                                                   (removed ? "Мёртвая запись удалена" : "Запись удалить не удалось"));
                return;
            }
        }

        StopProcesses(definition);
        step.Details = $"Удаляется {product.DisplayName}…";

        var status = new Progress<string>(message => step.Details = message);
        var result = await _installerService.RunAsync(file, arguments, Action, status, cancellationToken);

        // Удаление КриптоПро, вставшее на остановке драйвера, Windows доделывает при перезагрузке (так и было на практике).
        if (result.InstallerHung)
        {
            step.Status = StatusLevel.Warning;
            step.NeedsReboot = true;
            step.Details = result.ErrorMessage;
            return;
        }

        // Код возврата может быть «нужна перезагрузка» или «уже удалено», поэтому итог определяет реестр.
        var stillListed = FindProduct(definition) is not null;

        // Деинсталлятор NSIS (Контур.Плагин) возвращает управление сразу, а запись о программе убирает через несколько секунд.
        for (var i = 0; stillListed && result.Success && i < 20; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            stillListed = FindProduct(definition) is not null;
        }

        step.NeedsReboot = result.Success && (result.RebootRequired || stillListed);

        if (!stillListed)
        {
            var settingsRemoved = RemoveProductData(definition);
            step.Status = StatusLevel.Ok;
            step.Details = (result.RebootRequired ? "Удалено, для завершения требуется перезагрузка компьютера" : "Удалено")
                           + (settingsRemoved > 0 ? $". Удалены настройки программы (папок: {settingsRemoved})" : string.Empty);
            return;
        }

        // Установщик отчитался об успехе, но запись о программе осталась: Windows убирает её только после перезагрузки.
        if (result.Success)
        {
            step.Status = StatusLevel.Warning;
            step.Details = "Удаление выполнено, но программа исчезнет из списка только после перезагрузки компьютера";
            _logger.Warning($"{Action}: {definition.Name}", "Удаление выполнено, запись остаётся до перезагрузки компьютера");
            return;
        }

        step.Status = StatusLevel.Error;
        step.Details = $"Не удалось удалить (код {result.ExitCode}). {result.ErrorMessage}".Trim();
    }

    /// <summary>
    /// Удаляет папки с настройками программы из <see cref="UninstallProductDefinition.DataPaths"/> (если они есть и путь безопасен);
    /// возвращает, сколько папок удалено. Ошибки не прерывают удаление: они попадают в журнал.
    /// </summary>
    private int RemoveProductData(UninstallProductDefinition definition)
    {
        var removed = 0;

        // Собственные ветки продукта удаляются целиком, даже с данными: они принадлежат только этому продукту.
        foreach (var entry in definition.RegistryPaths)
        {
            if (!LeftoverCleanup.TryParseBranch(entry, out var hive, out var subKey))
            {
                continue;
            }

            try
            {
                hive.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
                _logger.Info($"{Action}: {definition.Name}", $"Удалена ветка реестра продукта: {subKey}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"{Action}: {definition.Name}", "Не удалось удалить ветку реестра продукта", $"{subKey}: {ex.Message}");
            }
        }
        foreach (var pattern in definition.DataPaths)
        {
            var path = Environment.ExpandEnvironmentVariables(pattern);
            if (!IsSafeToDelete(path) || !Directory.Exists(path))
            {
                continue;
            }

            try
            {
                Directory.Delete(path, recursive: true);
                removed++;
                _logger.Info($"{Action}: {definition.Name}", $"Удалена папка с настройками: {path}");

                // Родительскую папку (например, «Security Code») убираем только если в ней ничего не осталось: чужие файлы
                // или папки (Secret Net Studio и т. п.) не дают её удалить. Без рекурсии — пустую папку и так не тронет лишнего.
                var parent = Path.GetDirectoryName(path);
                if (parent is not null && IsSafeToDelete(parent) && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                {
                    Directory.Delete(parent);
                    _logger.Info($"{Action}: {definition.Name}", $"Удалена пустая родительская папка: {parent}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"{Action}: {definition.Name}", "Не удалось удалить папку с настройками", $"{path}: {ex.Message}");
            }
        }

        return removed;
    }

    /// <summary>
    /// Деинсталлятор с тем же именем в соседней папке (например, у Контур.Плагина файлы лежат в подпапке версии, а запись в реестре
    /// указывает на старую). Берётся самый свежий найденный; null — такого файла нет.
    /// </summary>
    internal static string? FindSiblingUninstaller(string missingFile)
    {
        try
        {
            var versionDirectory = Path.GetDirectoryName(missingFile);
            var parent = versionDirectory is null ? null : Directory.GetParent(versionDirectory);
            if (parent is null || !parent.Exists)
            {
                return null;
            }

            return parent.EnumerateDirectories()
                .Select(directory => Path.Combine(directory.FullName, Path.GetFileName(missingFile)))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// MSI удаляется по коду продукта через /x (UninstallString у КриптоПро содержит /I — это «изменить»).
    /// У EXE берётся команда из реестра плюс дополнительные ключи из конфигурации.
    /// </summary>
    internal static (string? File, string Arguments) BuildCommand(InstalledProductInfo product, UninstallProductDefinition definition)
    {
        if (definition.Kind == InstallerKind.Msi)
        {
            // MSIRESTARTMANAGERCONTROL=Disable: иначе «Диспетчер перезапуска» Windows Installer закрывает приложение, держащее файлы
            // КриптоПро (процесс подгружает их сам), и удаление обрывается. Занятые файлы удалятся при перезагрузке.
            var code = ExtractProductCode(product.KeyName) ?? ExtractProductCode(product.UninstallString);
            if (code is not null)
            {
                return ("msiexec.exe", $"/x {code} /qn /norestart MSIRESTARTMANAGERCONTROL=Disable {definition.Args}".Trim());
            }

            // Кода продукта нет (установщик-оболочка, не MSI в «чистом» виде): удаляем командой из реестра с ключами из Args.
            var (fallbackPath, fallbackRest) = SplitCommand(product.UninstallString);
            return fallbackPath is null ? (null, string.Empty) : (fallbackPath, $"{fallbackRest} {definition.Args}".Trim());
        }

        var (path, rest) = SplitCommand(product.UninstallString);
        return path is null ? (null, string.Empty) : (path, $"{rest} {definition.Args}".Trim());
    }

    internal static string? ExtractProductCode(string? text)
    {
        var match = string.IsNullOrEmpty(text) ? null : ProductCodePattern.Match(text);
        return match is { Success: true } ? match.Value : null;
    }

    /// <summary>Делит строку UninstallString на путь к exe и аргументы (путь может быть без кавычек и с пробелами).</summary>
    internal static (string? Path, string Arguments) SplitCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return (null, string.Empty);
        }

        command = command.Trim();
        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            if (end > 0)
            {
                return (command[1..end], command[(end + 1)..].Trim());
            }
        }

        if (File.Exists(command))
        {
            return (command, string.Empty);
        }

        var match = Regex.Match(command, @"^(.*?\.exe)(\s+.*)?$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? (match.Groups[1].Value, match.Groups[2].Value.Trim()) : (null, string.Empty);
    }

    private static void StopProcesses(UninstallProductDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ProcessPathContains))
        {
            return;
        }

        var self = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (process.Id != self && path?.Contains(definition.ProcessPathContains, StringComparison.OrdinalIgnoreCase) == true)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(10_000);
                }
            }
            catch (Exception)
            {
                // Нет доступа к процессу или он уже завершился — деинсталлятор сам сообщит, если файлы заняты.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    // ---------- Политика расширений ----------

    /// <summary>Записи форс-листа, добавленные приложением: те, что указывают на наш локальный updates.xml.</summary>
    internal static bool IsOurPolicyEntry(string? value) =>
        value?.Contains(OurUpdatesManifest, StringComparison.OrdinalIgnoreCase) == true;

    private IEnumerable<string> PolicyKeys() =>
        _configService.Current.Extensions.PolicyRegistryKeys
            .Append(LegacyPolicyKey)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private List<(string KeyPath, string ValueName)> FindPolicyEntries()
    {
        var entries = new List<(string, string)>();
        foreach (var keyPath in PolicyKeys())
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                if (key is null)
                {
                    continue;
                }

                entries.AddRange(key.GetValueNames()
                    .Where(name => IsOurPolicyEntry(key.GetValue(name) as string))
                    .Select(name => (keyPath, name)));
            }
            catch (Exception)
            {
                // Ветка недоступна — пропускаем.
            }
        }

        return entries;
    }

    private void RemoveExtensionPolicy(SetupStepModel step)
    {
        var removed = 0;
        foreach (var keyPath in PolicyKeys())
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: true);
            if (key is null)
            {
                continue;
            }

            foreach (var name in key.GetValueNames().Where(n => IsOurPolicyEntry(key.GetValue(n) as string)).ToList())
            {
                key.DeleteValue(name);
                removed++;
            }

            // Пустую ветку, которую создавало приложение, убираем; чужие записи остаются нетронутыми.
            if (key.ValueCount == 0 && key.SubKeyCount == 0)
            {
                var separator = keyPath.LastIndexOf('\\');
                using var parent = Registry.LocalMachine.OpenSubKey(keyPath[..separator], writable: true);
                parent?.DeleteSubKey(keyPath[(separator + 1)..], throwOnMissingSubKey: false);
            }
        }

        step.Status = StatusLevel.Ok;
        step.Details = removed == 0 ? "Записей приложения нет" : $"Удалено записей: {removed}";
        _logger.Info(Action, $"Политика расширений: удалено записей {removed}");
    }

    // ---------- Сертификаты ----------

    private static List<X509Certificate2> FindCryptoProCertificates()
    {
        var result = new List<X509Certificate2>();
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            result.AddRange(store.Certificates.Where(CertificateKeyProvider.IsCryptoPro));
        }
        catch (Exception)
        {
            // Хранилище недоступно — считаем, что сертификатов нет.
        }

        return result;
    }

    private void RemovePersonalCertificates(SetupStepModel step)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);

        var removed = 0;
        foreach (var certificate in store.Certificates.Where(CertificateKeyProvider.IsCryptoPro).ToList())
        {
            _logger.Info(Action, $"Удалён сертификат из «Личное»: {certificate.GetNameInfo(X509NameType.SimpleName, false)} ({certificate.Thumbprint})");
            store.Remove(certificate);
            removed++;
        }

        step.Status = StatusLevel.Ok;
        step.Details = removed == 0 ? "Сертификатов КриптоПро нет" : $"Удалено сертификатов: {removed}. Ключи и токен не затронуты";
    }

    // ---------- Остаточные данные ----------

    private List<string> ResolveLeftoverDirectories()
    {
        var found = new List<string>();
        var patterns = _configService.Current.Uninstall.LeftoverPaths
            .Append(_configService.Current.Extensions.RepositoryDirectory)
            .Append(@"%TEMP%\SetupAssistant");

        foreach (var pattern in patterns)
        {
            foreach (var path in ExpandPattern(Environment.ExpandEnvironmentVariables(pattern)))
            {
                if (IsSafeToDelete(path) && !found.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(path);
                }
            }
        }

        return found;
    }

    /// <summary>Раскрывает «*» в последнем сегменте пути; возвращает только существующие каталоги.</summary>
    internal static IEnumerable<string> ExpandPattern(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Contains('*'))
        {
            var parent = Path.GetDirectoryName(path);
            return parent is not null && Directory.Exists(parent)
                ? Directory.EnumerateDirectories(parent, name).ToList()
                : Enumerable.Empty<string>();
        }

        return Directory.Exists(path) ? new[] { path } : Enumerable.Empty<string>();
    }

    /// <summary>Защита от опасных путей: корни дисков, системные и пользовательские каталоги целиком, слишком короткие пути.</summary>
    internal static bool IsSafeToDelete(string path)
    {
        try
        {
            // Относительные пути и «C:foo» запрещены: GetFullPath привязал бы их к текущей папке процесса.
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                return false;
            }

            var full = Path.GetFullPath(path).TrimEnd('\\');
            var root = Path.GetPathRoot(full)?.TrimEnd('\\') ?? string.Empty;
            if (full.Length <= root.Length)
            {
                return false;
            }

            var depth = full[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
            if (depth < 2)
            {
                return false;
            }

            var protectedFolders = new[]
            {
                Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData,
                Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.MyDocuments
            }.Select(f => Environment.GetFolderPath(f).TrimEnd('\\'))
             .Append(Path.GetTempPath().TrimEnd('\\'))
             .Append(@"C:\Users");

            return !protectedFolders.Any(p => p.Length > 0 && string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private List<(RegistryKey Hive, string SubKey)> ResolveLeftoverRegistryKeys()
    {
        var found = new List<(RegistryKey, string)>();
        foreach (var entry in _configService.Current.Uninstall.LeftoverRegistryKeys)
        {
            if (!TryParseRegistryPath(entry, out var hive, out var subKey))
            {
                continue;
            }

            using var key = hive.OpenSubKey(subKey);
            if (key is not null)
            {
                found.Add((hive, subKey));
            }
        }

        return found;
    }

    /// <summary>Принимает только HKCU\… и HKLM\… глубиной не меньше двух сегментов (например, SOFTWARE\SKBKontur).</summary>
    internal static bool TryParseRegistryPath(string path, out RegistryKey hive, out string subKey)
    {
        hive = Registry.CurrentUser;
        subKey = string.Empty;

        var separator = path.IndexOf('\\');
        if (separator <= 0)
        {
            return false;
        }

        var prefix = path[..separator];
        if (prefix.Equals("HKCU", StringComparison.OrdinalIgnoreCase)) hive = Registry.CurrentUser;
        else if (prefix.Equals("HKLM", StringComparison.OrdinalIgnoreCase)) hive = Registry.LocalMachine;
        else return false;

        subKey = path[(separator + 1)..].Trim('\\');
        var segments = subKey.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        // Ветка «Crypto Pro» хранит ключевые контейнеры пользователя — её не трогаем ни при каких настройках.
        if (segments.Any(s => s.Equals("Crypto Pro", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Общие разделы, где один неверный путь в конфигурации навредил бы системе или чужим программам.
        var sharedVendors = new[] { "Microsoft", "Classes", "Policies", "WOW6432Node", "Google", "Mozilla" };
        return !sharedVendors.Contains(segments[1], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Папки из конфигурации, в которых не осталось ни одного файла.</summary>
    private List<string> ResolveEmptyFolders() =>
        _configService.Current.Uninstall.EmptyLeftoverFolders
            .Select(Environment.ExpandEnvironmentVariables)
            .Where(path => IsSafeToDelete(path) && LeftoverCleanup.IsEmptyFolderTree(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Ветки реестра из конфигурации, в которых нет ни одного значения.</summary>
    private List<(RegistryKey Hive, string SubKey)> ResolveEmptyRegistryBranches()
    {
        var found = new List<(RegistryKey, string)>();
        foreach (var entry in _configService.Current.Uninstall.EmptyLeftoverRegistryKeys)
        {
            if (!LeftoverCleanup.TryParseBranch(entry, out var hive, out var subKey))
            {
                continue;
            }

            try
            {
                using var key = hive.OpenSubKey(subKey);
                if (key is not null && !LeftoverCleanup.RegistryTreeHasValues(key))
                {
                    found.Add((hive, subKey));
                }
            }
            catch (Exception)
            {
                // Нет доступа к ветке — не трогаем её; добивка не должна ронять удаление.
            }
        }

        return found;
    }

    /// <summary>Осиротевшие ProgID с заданными префиксами (без CLSID или с CLSID несуществующего класса).</summary>
    private List<string> ResolveOrphanProgIds()
    {
        var found = new List<string>();
        foreach (var classesRoot in new[] { @"SOFTWARE\Classes", @"SOFTWARE\WOW6432Node\Classes" })
        {
            try
            {
                using var classes = Registry.LocalMachine.OpenSubKey(classesRoot);
                if (classes is null)
                {
                    continue;
                }

                foreach (var prefix in _configService.Current.Uninstall.OrphanProgIdPrefixes.Where(p => p.Length >= 6 && p.EndsWith('.')))
                {
                    foreach (var name in classes.GetSubKeyNames().Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        using var progId = classes.OpenSubKey(name);
                        if (progId is not null && LeftoverCleanup.IsOrphanProgId(progId))
                        {
                            found.Add($@"{classesRoot}\{name}");
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Нет доступа к разделу классов — пропускаем; добивка не должна ронять удаление.
            }
        }

        return found;
    }

    /// <param name="cryptoProOnly">Только следы КриптоПро (кэш установщика) и пустые оболочки: данные других программ не трогаются.</param>
    private void RemoveLeftovers(SetupStepModel step, bool cryptoProOnly = false)
    {
        var folders = 0;
        var keys = 0;
        var problems = new List<string>();

        foreach (var directory in ResolveLeftoverDirectories().Where(d => !cryptoProOnly || d.Contains("Crypto Pro", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                folders++;
                _logger.Info(Action, $"Удалена папка: {directory}");
            }
            catch (Exception ex)
            {
                problems.Add($"{directory}: {ex.Message}");
            }
        }

        foreach (var (hive, subKey) in cryptoProOnly ? new List<(RegistryKey Hive, string SubKey)>() : ResolveLeftoverRegistryKeys())
        {
            try
            {
                hive.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
                keys++;
                _logger.Info(Action, $"Удалена ветка реестра: {subKey}");
            }
            catch (Exception ex)
            {
                problems.Add($"{subKey}: {ex.Message}");
            }
        }

        // Пустые оболочки: папки без файлов и ветки без значений (в них нет данных, поэтому это безопасно и для «Crypto Pro»).
        foreach (var directory in ResolveEmptyFolders())
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                folders++;
                _logger.Info(Action, $"Удалена пустая папка: {directory}");
            }
            catch (Exception ex)
            {
                problems.Add($"{directory}: {ex.Message}");
            }
        }

        foreach (var (hive, subKey) in ResolveEmptyRegistryBranches())
        {
            try
            {
                hive.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
                keys++;
                _logger.Info(Action, $"Удалена пустая ветка реестра: {subKey}");
            }
            catch (Exception ex)
            {
                problems.Add($"{subKey}: {ex.Message}");
            }
        }

        foreach (var progId in ResolveOrphanProgIds())
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(progId, throwOnMissingSubKey: false);
                keys++;
                _logger.Info(Action, $"Удалён осиротевший ProgID: {progId}");
            }
            catch (Exception ex)
            {
                problems.Add($"{progId}: {ex.Message}");
            }
        }

        // Службу-драйвер КриптоПро убираем и при обычном удалении: запись службы без файла драйвера иначе остаётся в реестре.
        if (!cryptoProOnly && RemoveStaleDriver("CProCtrl"))
        {
            step.NeedsReboot = true;
            keys++;
        }

        step.Status = problems.Count == 0 ? StatusLevel.Ok : StatusLevel.Warning;
        step.Details = $"Удалено папок: {folders}, веток реестра: {keys}"
                       + (problems.Count == 0 ? string.Empty : $". Не удалось (возможно, заняты): {string.Join("; ", problems)}");
        foreach (var problem in problems)
        {
            _logger.Warning(Action, "Не удалось удалить остаток", problem);
        }
    }

    // ---------- Кэш приложения ----------

    private void RemoveAppCache(SetupStepModel step)
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SetupAssistant");
        var cache = Path.Combine(appData, "Payload");

        try
        {
            if (IsSafeToDelete(cache) && Directory.Exists(cache))
            {
                Directory.Delete(cache, recursive: true);
            }

            if (Directory.Exists(appData) && !Directory.EnumerateFileSystemEntries(appData).Any())
            {
                Directory.Delete(appData);
            }

            step.Status = StatusLevel.Ok;
            step.Details = "Кэш очищен";
        }
        catch (Exception ex)
        {
            step.Status = StatusLevel.Warning;
            step.Details = $"Кэш удалён не полностью (файлы заняты работающим приложением): {ex.Message}";
        }
    }

    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\}")]
    private static partial Regex ProductCodeRegex();
}
