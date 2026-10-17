namespace SetupAssistant.Services.Interfaces;

/// <summary>Пакет для поддержки: один zip с журналами, результатами диагностики и сведениями о системе.</summary>
public interface ISupportBundleService
{
    /// <summary>
    /// Собирает пакет в указанный файл. PIN-коды и закрытые ключи в пакет не попадают ни при каких настройках.
    /// </summary>
    /// <param name="includeCertificates">Добавить список сертификатов (владелец, организация, серийный номер, срок) — это персональные данные.</param>
    Task<string> CreateAsync(string zipPath, bool includeCertificates, CancellationToken cancellationToken = default);
}
