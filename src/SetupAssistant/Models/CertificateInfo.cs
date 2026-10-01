namespace SetupAssistant.Models;

/// <summary>Сведения о сертификате электронной подписи, найденном на носителе или в хранилище.</summary>
public sealed class CertificateInfo
{
    public string Owner { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string Thumbprint { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public bool IsInstalledToStore { get; set; }
    public bool HasPrivateKey { get; set; }

    /// <summary>Назначение ключа (Key Usage) в виде текста; пусто, если расширения нет.</summary>
    public string KeyUsage { get; set; } = string.Empty;

    /// <summary>Расширенное назначение (EKU): названия или OID через запятую.</summary>
    public string ExtendedKeyUsage { get; set; } = string.Empty;

    /// <summary>OID политик сертификата (Certificate Policies) через запятую.</summary>
    public string PolicyOids { get; set; } = string.Empty;

    /// <summary>Удалось ли построить цепочку доверия до корневого сертификата (без проверки отзыва).</summary>
    public bool IsChainValid { get; set; }

    /// <summary>Замечания проверки: назначение ключа, цепочка доверия. Пусто, если всё в порядке.</summary>
    public string ValidationMessage { get; set; } = string.Empty;

    public string ValidationSummary => string.IsNullOrEmpty(ValidationMessage) ? "Проверка пройдена" : ValidationMessage;

    public bool IsExpired => DateTime.Now > ValidTo;
    public int DaysUntilExpiry => (ValidTo - DateTime.Now).Days;
}
