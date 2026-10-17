namespace SetupAssistant.Models;

/// <summary>Какой PIN-код носителя меняется.</summary>
public enum PinKind
{
    User,
    Administrator
}

/// <summary>Носитель электронной подписи, найденный через PKCS#11-библиотеку производителя.</summary>
public sealed record PinTokenInfo(
    string Provider,
    string LibraryPath,
    ulong SlotId,
    string Label,
    string Model,
    string SerialNumber,
    bool UserPinLocked,
    bool UserPinFinalTry,
    bool UserPinCountLow,
    bool UserPinToBeChanged,
    bool SoPinLocked,
    bool SoPinFinalTry,
    bool SoPinCountLow)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Label)
            ? $"{Model} · № {SerialNumber}"
            : $"{Label} ({Model} · № {SerialNumber})";

    public bool IsPinLocked(PinKind kind) => kind == PinKind.User ? UserPinLocked : SoPinLocked;

    public bool IsFinalTry(PinKind kind) => kind == PinKind.User ? UserPinFinalTry : SoPinFinalTry;

    public bool IsCountLow(PinKind kind) => kind == PinKind.User ? UserPinCountLow : SoPinCountLow;

    /// <summary>Один и тот же физический носитель: слот может поменять номер, серийный номер — нет.</summary>
    public bool IsSameToken(PinTokenInfo other) =>
        Provider == other.Provider && SerialNumber == other.SerialNumber && SlotId == other.SlotId;
}

/// <summary>Результат поиска носителей: найденные драйверы и подключённые носители.</summary>
/// <param name="Providers">Названия найденных драйверов (Рутокен, JaCarta); пусто — ни один не установлен.</param>
/// <param name="Problem">Ошибка чтения носителей, если она была.</param>
public sealed record PinScanResult(IReadOnlyList<PinTokenInfo> Tokens, IReadOnlyList<string> Providers, string? Problem);

/// <summary>Результат смены PIN-кода; Message всегда понятен пользователю и не содержит PIN-кодов.</summary>
public sealed record PinChangeResult(bool Success, string Message);
