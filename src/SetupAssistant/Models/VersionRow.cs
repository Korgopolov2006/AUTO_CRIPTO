namespace SetupAssistant.Models;

/// <summary>Строка экрана «Версии»: что стоит, что допустимо и рекомендуется, что актуально на сервере и вывод по этим цифрам.</summary>
public sealed record VersionRow(
    string Name,
    string Installed,
    string Minimum,
    string Recommended,
    string Latest,
    StatusLevel Level,
    string Verdict,
    string Note);
