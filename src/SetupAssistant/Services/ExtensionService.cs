using System.IO;
using System.Text;
using Microsoft.Win32;
using SetupAssistant.Helpers;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IExtensionService"/>
/// <remarks>
/// Схема офлайн-установки: .crx копируются в постоянный локальный репозиторий
/// (%ProgramData%\SetupAssistant\Extensions), рядом создаётся update-манифест updates.xml,
/// после чего ID расширений вносятся в ExtensionInstallForcelist каждого поддерживаемого
/// браузера со ссылкой на локальный манифест (file:///...). Браузер устанавливает
/// расширения при следующем запуске без обращения к интернету.
/// </remarks>
public sealed class ExtensionService : IExtensionService
{
    private const string UpdatesManifestFileName = "updates.xml";

    private readonly IConfigService _configService;
    private readonly ILoggingService _logger;

    public ExtensionService(IConfigService configService, ILoggingService logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public Task<InstallResult> InstallExtensionsAsync(CancellationToken cancellationToken = default)
    {
        var config = _configService.Current.Extensions;

        try
        {
            var repositoryDir = _configService.ResolvePath(config.RepositoryDirectory);
            Directory.CreateDirectory(repositoryDir);

            var installed = new List<(ExtensionDefinition Definition, string Id, string CrxFileName)>();

            foreach (var extension in config.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var id = ResolveExtensionId(extension);
                if (id is null)
                {
                    _logger.Warning("Установка расширений", $"«{extension.Name}»: не задан Id и не удалось вычислить его из .pem — пропущено");
                    continue;
                }

                var crxSource = _configService.ResolvePath(extension.CrxPath);
                if (!File.Exists(crxSource))
                {
                    _logger.Warning("Установка расширений", $"«{extension.Name}»: файл не найден: {crxSource} — пропущено");
                    continue;
                }

                var crxFileName = $"{id}.crx";
                File.Copy(crxSource, Path.Combine(repositoryDir, crxFileName), overwrite: true);
                installed.Add((extension, id, crxFileName));
            }

            if (installed.Count == 0)
            {
                const string message = "Ни одно расширение не удалось подготовить к установке";
                _logger.Error("Установка расширений", message);
                return Task.FromResult(new InstallResult(false, -1, message));
            }

            var manifestPath = Path.Combine(repositoryDir, UpdatesManifestFileName);
            File.WriteAllText(manifestPath, BuildUpdatesManifest(repositoryDir, installed), Encoding.UTF8);

            var manifestUrl = ToFileUrl(manifestPath);
            foreach (var policyKey in config.PolicyRegistryKeys)
            {
                foreach (var (_, id, _) in installed)
                {
                    EnsureForceInstallEntry(policyKey, id, manifestUrl);
                }
            }

            _logger.Info("Установка расширений",
                $"Подготовлено расширений: {installed.Count}; политика применена к веткам: {config.PolicyRegistryKeys.Count}");
            return Task.FromResult(new InstallResult(true, 0, null));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("Установка расширений", "Ошибка применения политики", ex.Message);
            return Task.FromResult(new InstallResult(false, -1, ex.Message));
        }
    }

    public IReadOnlyList<ComponentInfo> CheckAllExtensions() =>
        _configService.Current.Extensions.Items.Select(CheckExtension).ToList();

    public ComponentInfo CheckCryptoProExtension() => CheckByRole(ExtensionRole.CryptoPro, "Расширение КриптоПро");

    public ComponentInfo CheckKonturExtension() => CheckByRole(ExtensionRole.Kontur, "Расширение Контур");

    private ComponentInfo CheckByRole(ExtensionRole role, string fallbackName)
    {
        var extension = _configService.Current.Extensions.Items.FirstOrDefault(e => e.Role == role);
        if (extension is not null)
        {
            return CheckExtension(extension);
        }

        return new ComponentInfo
        {
            Type = role == ExtensionRole.Kontur ? ComponentType.BrowserExtensionKontur : ComponentType.BrowserExtensionCryptoPro,
            Name = fallbackName,
            Description = "Расширение браузера для работы с электронной подписью",
            Status = StatusLevel.Warning,
            ErrorMessage = "Расширение не описано в конфигурации"
        };
    }

    private ComponentInfo CheckExtension(ExtensionDefinition extension)
    {
        var config = _configService.Current.Extensions;
        var id = ResolveExtensionId(extension);

        var registeredInPolicy = id is not null &&
            config.PolicyRegistryKeys.Any(key => IsExtensionRegistered(key, id));

        var crxInRepository = id is not null &&
            File.Exists(Path.Combine(_configService.ResolvePath(config.RepositoryDirectory), $"{id}.crx"));

        var installed = registeredInPolicy && crxInRepository;

        return new ComponentInfo
        {
            Type = extension.Role switch
            {
                ExtensionRole.Kontur => ComponentType.BrowserExtensionKontur,
                _ => ComponentType.BrowserExtensionCryptoPro
            },
            Name = $"Расширение: {extension.Name}",
            InstalledVersion = installed ? extension.Version : null,
            RecommendedVersion = extension.Version,
            Description = "Расширение браузера для работы с электронной подписью на веб-порталах",
            Status = installed ? StatusLevel.Ok : StatusLevel.Error,
            ErrorMessage = installed
                ? null
                : registeredInPolicy
                    ? "Файл расширения отсутствует в локальном репозитории"
                    : "Расширение не зарегистрировано в политике браузера"
        };
    }

    private string? ResolveExtensionId(ExtensionDefinition extension)
    {
        if (!string.IsNullOrWhiteSpace(extension.Id))
        {
            return extension.Id;
        }

        return ExtensionIdCalculator.TryComputeFromPemFile(_configService.ResolvePath(extension.PemPath));
    }

    private static string BuildUpdatesManifest(
        string repositoryDir, IReadOnlyList<(ExtensionDefinition Definition, string Id, string CrxFileName)> extensions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version='1.0' encoding='UTF-8'?>");
        sb.AppendLine("<gupdate xmlns='http://www.google.com/update2/response' protocol='2.0'>");

        foreach (var (definition, id, crxFileName) in extensions)
        {
            var codebase = ToFileUrl(Path.Combine(repositoryDir, crxFileName));
            sb.AppendLine($"  <app appid='{id}'>");
            sb.AppendLine($"    <updatecheck codebase='{codebase}' version='{definition.Version}' />");
            sb.AppendLine("  </app>");
        }

        sb.AppendLine("</gupdate>");
        return sb.ToString();
    }

    private static string ToFileUrl(string path) => new Uri(path).AbsoluteUri;

    private static bool IsExtensionRegistered(string policyKeyPath, string extensionId)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(policyKeyPath);
            if (key is null)
            {
                return false;
            }

            return key.GetValueNames()
                .Select(name => key.GetValue(name) as string)
                .Any(value => value?.StartsWith(extensionId, StringComparison.OrdinalIgnoreCase) == true);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Добавляет запись форс-листа, если её ещё нет; существующая запись перезаписывается новым URL манифеста.</summary>
    private static void EnsureForceInstallEntry(string policyKeyPath, string extensionId, string updateUrl)
    {
        using var key = Registry.LocalMachine.CreateSubKey(policyKeyPath, writable: true);

        var entryValue = $"{extensionId};{updateUrl}";

        var existingName = key.GetValueNames()
            .FirstOrDefault(name => (key.GetValue(name) as string)?.StartsWith(extensionId, StringComparison.OrdinalIgnoreCase) == true);

        if (existingName is not null)
        {
            key.SetValue(existingName, entryValue, RegistryValueKind.String);
            return;
        }

        var nextIndex = key.GetValueNames()
            .Select(name => int.TryParse(name, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        key.SetValue(nextIndex.ToString(), entryValue, RegistryValueKind.String);
    }
}
