using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;
using SetupAssistant.Helpers;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="ICertificateService"/>
public sealed class CertificateService : ICertificateService
{
    private const string ImportAction = "Установка сертификатов с носителя";

    // -absorb -certs: сертификаты из контейнеров в хранилище «Личное» с привязкой к закрытому ключу;
    // -autoprov: тип провайдера определяется по алгоритму ключа (ГОСТ 2001/2012).
    private const string CsptestArguments = "-absorb -certs -autoprov";

    // Запас на ввод PIN-кода носителя в окне КриптоПро.
    private static readonly TimeSpan CsptestTimeout = TimeSpan.FromMinutes(3);

    private readonly IConfigService _configService;
    private readonly ILoggingService _logger;

    public CertificateService(IConfigService configService, ILoggingService logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public IReadOnlyList<CertificateInfo> GetCertificates()
    {
        var results = new List<CertificateInfo>();

        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            try
            {
                store.Open(OpenFlags.ReadOnly);
            }
            catch (Exception ex)
            {
                _logger.Warning("Чтение хранилища сертификатов", $"Не удалось открыть хранилище {location}", ex.Message);
                continue;
            }

            foreach (var certificate in store.Certificates)
            {
                results.Add(MapCertificate(certificate));
            }
        }

        return results
            .GroupBy(c => c.Thumbprint)
            .Select(g => g.First())
            .OrderByDescending(c => c.ValidTo)
            .ToList();
    }

    public Task<bool> InstallCertificateAsync(CertificateInfo certificate, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);

                var existing = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false);
                if (existing.Count == 0)
                {
                    _logger.Error("Установка сертификата", "Сертификат не найден в исходном хранилище", certificate.Thumbprint);
                    return false;
                }

                using var rootStore = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                rootStore.Open(OpenFlags.ReadWrite);
                if (!rootStore.Certificates.Contains(existing[0]))
                {
                    rootStore.Add(existing[0]);
                }

                _logger.Info("Установка сертификата", $"Сертификат {certificate.Owner} успешно установлен");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("Установка сертификата", "Ошибка установки", ex.Message);
                return false;
            }
        }, cancellationToken);
    }

    public async Task<TokenCertificateImportResult> ImportFromContainersAsync(CancellationToken cancellationToken = default)
    {
        var csptestPath = CsptestRunner.FindPath();
        if (csptestPath is null)
        {
            _logger.Error(ImportAction, "Не выполнена", "csptest.exe не найден, КриптоПро CSP не установлен");
            return new TokenCertificateImportResult(StatusLevel.Error, "Не найден csptest.exe: КриптоПро CSP не установлен");
        }

        var thumbprintsBefore = GetCertificates()
            .Select(c => c.Thumbprint)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        CsptestRun run;
        try
        {
            run = await CsptestRunner.RunAsync(csptestPath, CsptestArguments, CsptestTimeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ImportAction, "Не удалось запустить csptest", ex.Message);
            return new TokenCertificateImportResult(StatusLevel.Error, $"Не удалось запустить csptest: {ex.Message}");
        }

        if (run.TimedOut)
        {
            _logger.Error(ImportAction, "Превышено время ожидания", run.Output);
            return new TokenCertificateImportResult(StatusLevel.Error,
                "Превышено время ожидания csptest. Возможно, КриптоПро ждал ввода PIN-кода носителя — повторите настройку.");
        }

        if (run.ExitCode != 0)
        {
            _logger.Error(ImportAction, $"csptest завершился с кодом {run.ExitCode}", run.Output);
            return new TokenCertificateImportResult(StatusLevel.Error,
                $"csptest завершился с кодом {run.ExitCode}. {Shorten(run.Output)}".Trim());
        }

        var certificatesAfter = GetCertificates();
        var added = certificatesAfter.Where(c => !thumbprintsBefore.Contains(c.Thumbprint)).ToList();

        if (added.Count > 0)
        {
            var owners = string.Join("; ", added.Take(3).Select(c => string.IsNullOrWhiteSpace(c.Owner) ? c.Thumbprint : c.Owner));
            var message = $"Установлено сертификатов: {added.Count} ({owners}{(added.Count > 3 ? "…" : string.Empty)})";
            _logger.Info(ImportAction, message);
            return new TokenCertificateImportResult(StatusLevel.Ok, message);
        }

        const string manualHint =
            "Если сертификат на носителе есть, но не установился, установите его вручную: КриптоПро CSP → Сервис → «Просмотреть сертификаты в контейнере» → «Установить».";
        var warning = certificatesAfter.Count == 0
            ? $"В хранилище «Личное» нет сертификатов: на носителе не найдено сертификатов. {manualHint}"
            : $"Новых сертификатов не появилось (уже установлены или на носителе их нет). {manualHint}";
        _logger.Warning(ImportAction, "Новых сертификатов не добавлено", run.Output);
        return new TokenCertificateImportResult(StatusLevel.Warning, warning);
    }

    private static string Shorten(string text)
    {
        var singleLine = string.Join(' ', text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= 300 ? singleLine : singleLine[..300] + "…";
    }

    public ComponentInfo CheckCertificatesStatus()
    {
        var certificates = GetCertificates();
        var warningDays = _configService.Current.CheckSettings.CertificateExpiryWarningDays;

        if (certificates.Count == 0)
        {
            return new ComponentInfo
            {
                Type = ComponentType.Certificate,
                Name = "Сертификаты электронной подписи",
                Description = "Сертификаты в личном хранилище пользователя",
                Status = StatusLevel.Error,
                ErrorMessage = "Сертификаты не найдены"
            };
        }

        var expired = certificates.Where(c => c.IsExpired).ToList();
        var expiringSoon = certificates.Where(c => !c.IsExpired && c.DaysUntilExpiry <= warningDays).ToList();

        var untrusted = certificates.Where(c => !c.IsExpired && !c.IsChainValid).ToList();

        var status = expired.Count == certificates.Count
            ? StatusLevel.Error
            : expiringSoon.Count > 0 || expired.Count > 0 || untrusted.Count > 0
                ? StatusLevel.Warning
                : StatusLevel.Ok;

        string? errorMessage = status switch
        {
            StatusLevel.Error => "Все найденные сертификаты просрочены",
            StatusLevel.Warning =>
                $"Просроченных: {expired.Count}, истекающих в ближайшее время: {expiringSoon.Count}, " +
                $"без построенной цепочки доверия: {untrusted.Count} (установите сертификаты УЦ)",
            _ => null
        };

        return new ComponentInfo
        {
            Type = ComponentType.Certificate,
            Name = "Сертификаты электронной подписи",
            Description = $"Найдено сертификатов: {certificates.Count}",
            Status = status,
            ErrorMessage = errorMessage
        };
    }

    private static CertificateInfo MapCertificate(X509Certificate2 certificate)
    {
        var info = new CertificateInfo
        {
            Owner = certificate.GetNameInfo(X509NameType.SimpleName, false),
            Organization = ExtractSubjectField(certificate, "O="),
            Container = certificate.GetNameInfo(X509NameType.SimpleName, false),
            SerialNumber = certificate.SerialNumber,
            Thumbprint = certificate.Thumbprint,
            ValidFrom = certificate.NotBefore,
            ValidTo = certificate.NotAfter,
            IsInstalledToStore = true,
            HasPrivateKey = certificate.HasPrivateKey
        };

        ValidateCertificate(certificate, info);
        return info;
    }

    /// <summary>Проверяет назначение ключа, EKU, политики и цепочку доверия. Отзыв не проверяется: для ГОСТ нужны CRL из сети УЦ.</summary>
    private static void ValidateCertificate(X509Certificate2 certificate, CertificateInfo info)
    {
        var problems = new List<string>();

        var keyUsage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (keyUsage is not null)
        {
            info.KeyUsage = keyUsage.KeyUsages.ToString();
            if (!keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature)
                && !keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.NonRepudiation))
            {
                problems.Add("назначение ключа не допускает электронную подпись");
            }
        }

        var eku = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        if (eku is not null)
        {
            info.ExtendedKeyUsage = string.Join(", ", eku.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>()
                .Select(o => string.IsNullOrEmpty(o.FriendlyName) ? o.Value : $"{o.FriendlyName} ({o.Value})"));
        }

        info.PolicyOids = ReadPolicyOids(certificate);

        try
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

            info.IsChainValid = chain.Build(certificate);
            if (!info.IsChainValid)
            {
                problems.AddRange(chain.ChainStatus
                    .Select(s => s.StatusInformation.Trim())
                    .Where(s => s.Length > 0)
                    .Distinct());
                problems.Add("проверьте, что корневой и промежуточные сертификаты УЦ установлены");
            }
        }
        catch (Exception ex)
        {
            info.IsChainValid = false;
            problems.Add($"не удалось построить цепочку доверия: {ex.Message}");
        }

        info.ValidationMessage = string.Join("; ", problems);
    }

    private static string ReadPolicyOids(X509Certificate2 certificate)
    {
        const string certificatePoliciesOid = "2.5.29.32";

        var extension = certificate.Extensions.Cast<X509Extension>().FirstOrDefault(e => e.Oid?.Value == certificatePoliciesOid);
        if (extension is null)
        {
            return string.Empty;
        }

        try
        {
            var policies = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
            var oids = new List<string>();
            while (policies.HasData)
            {
                oids.Add(policies.ReadSequence().ReadObjectIdentifier());
            }

            return string.Join(", ", oids);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string ExtractSubjectField(X509Certificate2 certificate, string fieldPrefix)
    {
        var part = certificate.Subject
            .Split(',')
            .Select(s => s.Trim())
            .FirstOrDefault(s => s.StartsWith(fieldPrefix, StringComparison.OrdinalIgnoreCase));

        return part is null ? string.Empty : part[fieldPrefix.Length..].Trim();
    }
}
