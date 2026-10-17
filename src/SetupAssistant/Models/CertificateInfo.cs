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

    /// <summary>Срок действия для индикатора в списке: Ok / Warning (скоро истекает) / Error (истёк). Заполняет экран сертификатов.</summary>
    public StatusLevel ExpiryLevel { get; set; } = StatusLevel.NotChecked;

    public string ExpiryText { get; set; } = string.Empty;

    /// <summary>Заполняет <see cref="ExpiryLevel"/> и <see cref="ExpiryText"/> по сроку действия.</summary>
    public void ApplyExpiry(int warningDays)
    {
        var days = (ValidTo.Date - DateTime.Now.Date).Days;
        (ExpiryLevel, ExpiryText) = days switch
        {
            < 0 => (StatusLevel.Error, $"Истёк {-days} дн. назад"),
            0 => (StatusLevel.Warning, "Истекает сегодня"),
            _ when days <= warningDays => (StatusLevel.Warning, $"Истекает через {days} дн."),
            _ => (StatusLevel.Ok, $"Действует ещё {days} дн.")
        };
    }

    /// <summary>Заполнение полоски срока: у истёкшего сертификата полоска полная и красная, у действующего — доля оставшегося срока.</summary>
    public double ValidityBarPercent => ExpiryLevel == StatusLevel.Error ? 100 : RemainingPercent;

    /// <summary>Сколько срока действия осталось (0..100) — для полоски в списке сертификатов.</summary>
    public double RemainingPercent
    {
        get
        {
            var total = (ValidTo - ValidFrom).TotalDays;
            if (total <= 0)
            {
                return 0;
            }

            return Math.Clamp((ValidTo - DateTime.Now).TotalDays / total * 100, 0, 100);
        }
    }

    public bool IsExpired => DateTime.Now > ValidTo;
    public int DaysUntilExpiry => (ValidTo - DateTime.Now).Days;
}
