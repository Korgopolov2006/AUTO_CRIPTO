namespace SetupAssistant.Models;

/// <summary>Производитель, чьи следы ищет полная очистка.</summary>
[Flags]
public enum CryptoVendor
{
    None = 0,
    CryptoPro = 1,
    Rutoken = 2,
    Kontur = 4,
    Gosuslugi = 8,

    /// <summary>Код Безопасности CSP и Континент-АП: чистятся только в режимах, где удаляется Континент.</summary>
    SecurityCode = 16,

    All = CryptoPro | Rutoken | Kontur | Gosuslugi | SecurityCode
}

/// <summary>Что именно удаляется. Общие разделы Windows правятся точечно: значение, элемент списка, функция CNG.</summary>
public enum CleanupItemKind
{
    /// <summary>Раздел реестра целиком.</summary>
    RegistryKey,

    /// <summary>Одно значение раздела.</summary>
    RegistryValue,

    /// <summary>Элемент списка REG_MULTI_SZ (например, пакет в «Security Packages» LSA); остальные элементы сохраняются.</summary>
    ListItem,

    /// <summary>Функция CNG в контексте конфигурации (BCryptRemoveContextFunction): у неё нет других поставщиков.</summary>
    CngFunction,

    /// <summary>Поставщик у функции CNG (BCryptRemoveContextFunctionProvider): у функции есть и другие поставщики, например Microsoft.</summary>
    CngFunctionProvider,

    /// <summary>Регистрация поставщика CNG (BCryptUnregisterProvider).</summary>
    CngProvider,

    /// <summary>Служба или драйвер: останавливается и удаляется через диспетчер служб.</summary>
    Service,

    File,

    Directory
}

/// <summary>Найденный след СКЗИ. Реестр записывается как HKLM\…, HKCU\… или HKU\… (64-битный вид, ветки WOW6432Node — явно).</summary>
public sealed class CleanupItem
{
    public required CleanupItemKind Kind { get; init; }

    public required CryptoVendor Vendor { get; init; }

    /// <summary>Раздел отчёта: «Криптопровайдеры CryptoAPI», «CNG и TLS», «Файлы» и т. п.</summary>
    public required string Category { get; init; }

    /// <summary>Раздел реестра, путь к файлу или папке, имя службы, контекст CNG.</summary>
    public required string Path { get; init; }

    /// <summary>Имя значения (<see cref="CleanupItemKind.RegistryValue"/>, <see cref="CleanupItemKind.ListItem"/>).</summary>
    public string? ValueName { get; init; }

    /// <summary>
    /// Элемент списка (<see cref="CleanupItemKind.ListItem"/>), имя функции CNG. У <see cref="CleanupItemKind.RegistryValue"/> —
    /// новое значение вместо удаления (тип провайдера по умолчанию переводится на оставшийся провайдер того же типа).
    /// </summary>
    public string? Item { get; init; }

    /// <summary>Интерфейс CNG (BCRYPT_*_INTERFACE, NCRYPT_SCHANNEL_INTERFACE и т. п.).</summary>
    public uint CngInterface { get; init; }

    /// <summary>Поставщик CNG для <see cref="CleanupItemKind.CngFunctionProvider"/>.</summary>
    public string? CngProvider { get; init; }

    /// <summary>Ключевые контейнеры или папка, где они лежат: удаляются, только если пользователь отдельно это подтвердил.</summary>
    public bool IsContainerData { get; init; }

    public override string ToString() => Kind switch
    {
        CleanupItemKind.RegistryValue when Item is not null => $@"{Path} → «{ValueName}» = «{Item}» (вместо удаляемого)",
        CleanupItemKind.RegistryValue => $@"{Path} → «{ValueName}»",
        CleanupItemKind.ListItem => $@"{Path} → «{ValueName}»: элемент «{Item}»",
        CleanupItemKind.CngFunction => $"CNG {Path}, интерфейс 0x{CngInterface:X8}: функция «{Item}»",
        CleanupItemKind.CngFunctionProvider => $"CNG {Path}, интерфейс 0x{CngInterface:X8}: у функции «{Item}» поставщик «{CngProvider}»",
        CleanupItemKind.CngProvider => $"Поставщик CNG «{Path}»",
        CleanupItemKind.Service => $"Служба/драйвер «{Path}»",
        _ => Path
    };
}

/// <summary>Ключевой контейнер КриптоПро на компьютере (в реестре или в папке считывателя HDIMAGE). Носители (токены) сюда не входят.</summary>
/// <param name="Name">Имя контейнера (раздел реестра или папка вида «name.000»).</param>
/// <param name="Location">Полный путь: раздел HKLM\… или папка.</param>
/// <param name="InRegistry">Контейнер хранится в реестре (считыватель «Реестр»).</param>
/// <param name="Owner">Чей контейнер: учётная запись или «компьютер».</param>
public sealed record KeyContainerInfo(string Name, string Location, bool InRegistry, string Owner);

/// <summary>
/// Расширение из списка приложения, установленное в профиль браузера на движке Chromium (вручную или из магазина). Полная очистка
/// его не удаляет: браузер хранит такие расширения в своём профиле, удалить их можно только в самом браузере.
/// </summary>
/// <param name="Browser">Браузер: «Chromium / Chromium GOST», «Google Chrome», «Microsoft Edge», «Яндекс.Браузер».</param>
/// <param name="Profile">Папка профиля браузера (Default, Profile 1).</param>
/// <param name="Owner">Пользователь Windows.</param>
public sealed record BrowserExtensionInfo(string Browser, string Profile, string Owner, string Id, string Name);

/// <summary>Состояние одного запуска полной очистки: папка отчёта, решение по контейнерам, отложенное продолжение.</summary>
public sealed class FullCleanupSession
{
    /// <summary>Папка отчёта: резервные копии контейнеров и удаляемых разделов реестра, report.txt.</summary>
    public required string BackupDirectory { get; init; }

    /// <summary>Очистка затрагивает и Код Безопасности / Континент-АП (кнопки «Удалить» и «КриптоПро с Континентом»).</summary>
    public bool IncludeKontinent { get; init; }

    /// <summary>Пользователь отдельно подтвердил удаление ключевых контейнеров (только после успешной резервной копии).</summary>
    public bool DeleteContainers { get; set; }

    /// <summary>Продолжение после перезагрузки: программы уже удалены, выполняется только глубокая очистка.</summary>
    public bool IsResume { get; init; }

    public List<KeyContainerInfo> Containers { get; } = new();

    /// <summary>Резервная копия всех найденных контейнеров сохранена.</summary>
    public bool ContainersBackedUp { get; set; }

    /// <summary>Глубокую очистку нужно выполнить (или повторить) после перезагрузки.</summary>
    public bool ContinueAfterReboot { get; set; }

    public CryptoVendor Vendors => IncludeKontinent ? CryptoVendor.All : CryptoVendor.All & ~CryptoVendor.SecurityCode;
}
