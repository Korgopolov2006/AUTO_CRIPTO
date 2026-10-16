using System.IO;
using System.Security.Cryptography.X509Certificates;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="ICaCertificateService"/>
public sealed class CaCertificateService : ICaCertificateService
{
    private const string Action = "Установка сертификатов УЦ";

    private readonly IConfigService _configService;
    private readonly ILoggingService _logger;

    public CaCertificateService(IConfigService configService, ILoggingService logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public int ConfiguredCount => _configService.Current.CaCertificates.Count;

    public Task<InstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var problems = new List<string>();
            var installed = 0;

            foreach (var definition in _configService.Current.CaCertificates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var problem = TryImport(definition);
                if (problem is null)
                {
                    installed++;
                }
                else
                {
                    problems.Add($"«{definition.Name}»: {problem}");
                    _logger.Error(Action, $"«{definition.Name}» не установлен", problem);
                }
            }

            _logger.Info(Action, $"Обработано сертификатов: {ConfiguredCount}, успешно: {installed}");
            return problems.Count == 0
                ? new InstallResult(true, 0, null)
                : new InstallResult(false, -1, string.Join("; ", problems));
        }, cancellationToken);
    }

    public ComponentInfo Check()
    {
        var definitions = _configService.Current.CaCertificates;
        var info = new ComponentInfo
        {
            Type = ComponentType.CaCertificates,
            Name = "Сертификаты УЦ",
            Description = "Корневые и промежуточные сертификаты удостоверяющих центров"
        };

        if (definitions.Count == 0)
        {
            info.Status = StatusLevel.NotChecked;
            info.ErrorMessage = "Сертификаты УЦ не заданы в Config.json (раздел CaCertificates)";
            return info;
        }

        var missing = definitions.Where(d => !IsInstalled(d)).Select(d => d.Name).ToList();
        info.Status = missing.Count == 0 ? StatusLevel.Ok : StatusLevel.Error;
        info.InstalledVersion = $"{definitions.Count - missing.Count} из {definitions.Count}";
        info.ErrorMessage = missing.Count == 0 ? null : $"Не установлены: {string.Join(", ", missing)}";
        return info;
    }

    /// <returns>null при успехе, иначе описание проблемы.</returns>
    private string? TryImport(CaCertificateDefinition definition)
    {
        if (!TryParseStore(definition.Store, out var location, out var storeName))
        {
            return $"неизвестное хранилище «{definition.Store}» (допустимо: LocalMachine\\Root, LocalMachine\\CA, CurrentUser\\Root, CurrentUser\\CA)";
        }

        var expectedThumbprint = NormalizeThumbprint(definition.Thumbprint);
        if (expectedThumbprint.Length == 0)
        {
            return "в конфигурации не задан ожидаемый отпечаток (Thumbprint), импорт без проверки запрещён";
        }

        var path = _configService.ResolvePath(definition.Path);
        if (!File.Exists(path))
        {
            return $"файл не найден: {path}";
        }

        X509Certificate2 certificate;
        try
        {
            certificate = new X509Certificate2(path);
        }
        catch (Exception ex)
        {
            return $"файл не является сертификатом: {ex.Message}";
        }

        using (certificate)
        {
            if (!string.Equals(NormalizeThumbprint(certificate.Thumbprint), expectedThumbprint, StringComparison.Ordinal))
            {
                return $"отпечаток файла ({certificate.Thumbprint}) не совпадает с ожидаемым ({expectedThumbprint})";
            }

            var now = DateTime.Now;
            if (now < certificate.NotBefore || now > certificate.NotAfter)
            {
                return $"срок действия: {certificate.NotBefore:dd.MM.yyyy} — {certificate.NotAfter:dd.MM.yyyy}, сертификат недействителен";
            }

            var basicConstraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
            if (basicConstraints is null || !basicConstraints.CertificateAuthority)
            {
                return "сертификат не является сертификатом удостоверяющего центра (нет признака CA)";
            }

            var keyUsage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
            if (keyUsage is not null && !keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign))
            {
                return "назначение ключа не допускает подпись сертификатов (нет KeyCertSign)";
            }

            var selfSigned = certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData);
            if (storeName == StoreName.Root && !selfSigned)
            {
                return "в хранилище корневых можно устанавливать только самоподписанный сертификат";
            }

            if (storeName == StoreName.CertificateAuthority && selfSigned)
            {
                return "самоподписанный корневой сертификат нельзя устанавливать в хранилище промежуточных";
            }

            try
            {
                using var store = new X509Store(storeName, location);
                store.Open(OpenFlags.ReadWrite);

                if (store.Certificates.Find(X509FindType.FindByThumbprint, expectedThumbprint, validOnly: false).Count == 0)
                {
                    store.Add(certificate);
                    _logger.Info(Action, $"«{definition.Name}» установлен в {definition.Store} (отпечаток {expectedThumbprint})");
                }
                else
                {
                    _logger.Info(Action, $"«{definition.Name}» уже установлен в {definition.Store}");
                }
            }
            catch (Exception ex)
            {
                return $"не удалось записать в хранилище {definition.Store}: {ex.Message}";
            }
        }

        return null;
    }

    private static bool IsInstalled(CaCertificateDefinition definition)
    {
        if (!TryParseStore(definition.Store, out var location, out var storeName))
        {
            return false;
        }

        var thumbprint = NormalizeThumbprint(definition.Thumbprint);
        if (thumbprint.Length == 0)
        {
            return false;
        }

        try
        {
            using var store = new X509Store(storeName, location);
            store.Open(OpenFlags.ReadOnly);
            return store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false).Count > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryParseStore(string value, out StoreLocation location, out StoreName name)
    {
        location = StoreLocation.LocalMachine;
        name = StoreName.Root;

        var parts = value.Split('\\', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        if (parts[0].Equals("LocalMachine", StringComparison.OrdinalIgnoreCase)) location = StoreLocation.LocalMachine;
        else if (parts[0].Equals("CurrentUser", StringComparison.OrdinalIgnoreCase)) location = StoreLocation.CurrentUser;
        else return false;

        if (parts[1].Equals("Root", StringComparison.OrdinalIgnoreCase)) name = StoreName.Root;
        else if (parts[1].Equals("CA", StringComparison.OrdinalIgnoreCase)) name = StoreName.CertificateAuthority;
        else return false;

        return true;
    }

    /// <summary>Оставляет только hex-символы (при копировании из окна сертификата попадают пробелы и невидимые знаки).</summary>
    private static string NormalizeThumbprint(string value) =>
        new(value.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
}
