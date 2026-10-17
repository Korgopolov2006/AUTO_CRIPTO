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
            Details = $"Найдено для очистки: папок {ResolveLeftoverDirectories().Count}, веток реестра {ResolveLeftoverRegistryKeys().Count}"
        });

        steps.Add(new SetupStepModel
        {
            Kind = SetupStepKind.RemoveAppCache,
            Title = "Очистка кэша приложения",
            Details = "Распакованные дистрибутивы и скачанные файлы"
        });

        return steps;
    }

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
            step.Status = StatusLevel.Ok;
            step.Details = "Не установлено";
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

        StopProcesses(definition);
        step.Details = $"Удаляется {product.DisplayName}…";

        var status = new Progress<string>(message => step.Details = message);
        var result = await _installerService.RunAsync(file, arguments, Action, status, cancellationToken);

        // Код возврата может быть «нужна перезагрузка» или «уже удалено», поэтому итог определяет реестр.
        var stillListed = FindProduct(definition) is not null;
        step.NeedsReboot = result.Success && (result.RebootRequired || stillListed);

        if (!stillListed)
        {
            step.Status = StatusLevel.Ok;
            step.Details = result.RebootRequired ? "Удалено, для завершения требуется перезагрузка компьютера" : "Удалено";
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
            return code is null
                ? (null, string.Empty)
                : ("msiexec.exe", $"/x {code} /qn /norestart MSIRESTARTMANAGERCONTROL=Disable {definition.Args}".Trim());
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

    private void RemoveLeftovers(SetupStepModel step)
    {
        var folders = 0;
        var keys = 0;
        var problems = new List<string>();

        foreach (var directory in ResolveLeftoverDirectories())
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

        foreach (var (hive, subKey) in ResolveLeftoverRegistryKeys())
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
