namespace SetupAssistant.Models;

/// <summary>Итог установки сертификатов из контейнеров закрытых ключей в хранилище «Личное».</summary>
public sealed record TokenCertificateImportResult(StatusLevel Status, string Message);
