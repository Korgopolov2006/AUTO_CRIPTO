namespace SetupAssistant.Models;

/// <summary>Корневая модель конфигурации приложения, загружаемая из Config/Config.json.</summary>
public sealed class AppConfig
{
    public string AppName { get; set; } = "Помощник настройки рабочего места для электронной подписи";

    /// <summary>Версия приложения из сборки; значение в Config.json игнорируется, чтобы номер не расходился с реальной сборкой.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string AppVersion { get; set; } = Helpers.AppVersionInfo.Current;

    public string LogoPath { get; set; } = "Resources/logo.png";

    public InstallersConfig Installers { get; set; } = new();
    public MinVersionsConfig MinVersions { get; set; } = new();
    public RecommendedVersionsConfig RecommendedVersions { get; set; } = new();
    public RequiredComponentsConfig RequiredComponents { get; set; } = new();
    public ExtensionsConfig Extensions { get; set; } = new();
    public NativeMessagingConfig NativeMessaging { get; set; } = new();
    public UninstallConfig Uninstall { get; set; } = new();
    public KontinentConfig Kontinent { get; set; } = new();
    public List<CaCertificateDefinition> CaCertificates { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
    public CheckSettingsConfig CheckSettings { get; set; } = new();
    public UiSettingsConfig UiSettings { get; set; } = new();
    public List<BrowserDefinition> SupportedBrowsers { get; set; } = new();
}

/// <summary>Тип пакета установки, определяющий способ тихого запуска.</summary>
public enum InstallerKind
{
    Exe,
    Msi
}

/// <summary>Описание одного устанавливаемого пакета: путь, тип и ключи тихой установки.</summary>
public sealed class InstallerDefinition
{
    /// <summary>Локальный путь к установщику; при заданном DownloadUrl служит запасным вариантом без сети.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Адрес последней версии; если задан — установщик скачивается при каждой установке.</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Если задан, DownloadUrl — адрес GitHub API «latest release», а это regex имени файла-ассета.</summary>
    public string DownloadAssetRegex { get; set; } = string.Empty;

    /// <summary>
    /// Адрес последней версии для утилиты обновления ExtensionUpdater: прямая ссылка либо GitHub API (при заданном
    /// DownloadAssetRegex). Если пусто — берётся DownloadUrl; если пусто и там — дистрибутив зафиксирован и не обновляется.
    /// </summary>
    public string UpdateUrl { get; set; } = string.Empty;

    /// <summary>Страница, которую нужно открыть перед скачиванием: сайт выдаёт файл только при наличии сессионной куки.</summary>
    public string UpdateWarmupUrl { get; set; } = string.Empty;

    /// <summary>Подстрока имени владельца цифровой подписи; скачанный файл с другой или недействительной подписью отвергается.</summary>
    public string ExpectedSigner { get; set; } = string.Empty;

    public InstallerKind Kind { get; set; } = InstallerKind.Exe;
    public string SilentArgs { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Path) || !string.IsNullOrWhiteSpace(DownloadUrl);
}

public sealed class InstallersConfig
{
    public InstallerDefinition CryptoProCsp { get; set; } = new();
    public InstallerDefinition CryptoProBrowserPlugin { get; set; } = new();
    public InstallerDefinition ChromiumGost { get; set; } = new();
    public InstallerDefinition KonturPlugin { get; set; } = new();
    public InstallerDefinition GosuslugiPlugin { get; set; } = new();

    /// <summary>Континент-АП (VPN-клиент «Код Безопасности»): дистрибутив кладёт заказчик; пока Path пуст, шаги установки в план не попадают.</summary>
    public InstallerDefinition KontinentAp { get; set; } = new();
    public InstallerDefinition RutokenDriver { get; set; } = new();
    public InstallerDefinition JaCartaDriver { get; set; } = new();
}

public sealed class MinVersionsConfig
{
    public string CryptoProCsp { get; set; } = "5.0.0";
    public string CryptoProBrowserPlugin { get; set; } = "2.0.0";
    public string ChromiumGost { get; set; } = "100.0.0";
    public string KonturPlugin { get; set; } = "1.0.0";
}

public sealed class RecommendedVersionsConfig
{
    public string CryptoProCsp { get; set; } = "5.0.12000";
    public string CryptoProBrowserPlugin { get; set; } = "2.0.14000";
    public string ChromiumGost { get; set; } = "148.0.7778";
    public string KonturPlugin { get; set; } = "1.0.0";
}

public sealed class RequiredComponentsConfig
{
    public bool CryptoProCsp { get; set; } = true;
    public bool CryptoProBrowserPlugin { get; set; } = true;
    public bool ChromiumGost { get; set; } = true;
    public bool KonturPlugin { get; set; } = true;
    public bool GosuslugiPlugin { get; set; } = true;
    public bool RutokenDriver { get; set; } = true;
    public bool JaCartaDriver { get; set; } = false;
}

/// <summary>Роль расширения — по ней диагностика находит обязательные расширения КриптоПро и Контур.</summary>
public enum ExtensionRole
{
    Other,
    CryptoPro,
    Kontur
}

/// <summary>Описание одного расширения браузера для офлайн-установки из локального .crx.</summary>
public sealed class ExtensionDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>ID расширения (32 символа a-p). Если пусто — вычисляется из PemPath.</summary>
    public string Id { get; set; } = string.Empty;

    public string CrxPath { get; set; } = string.Empty;

    /// <summary>Путь к .pem с ключом упаковки — используется для вычисления ID, когда Id не задан.</summary>
    public string PemPath { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public ExtensionRole Role { get; set; } = ExtensionRole.Other;

    /// <summary>
    /// ID расширения в Chrome Web Store (вычисляется из поля key в manifest.json). Отличается от Id,
    /// если .crx перепакован собственным ключом. Нужен только для проверки новых версий.
    /// </summary>
    public string StoreId { get; set; } = string.Empty;
}

/// <summary>Результат сравнения локальной версии расширения с версией в магазине.</summary>
public sealed record ExtensionUpdateInfo(
    string Name,
    string CurrentVersion,
    string? LatestVersion,
    bool IsUpdateAvailable,
    string? Error)
{
    public string Summary => Error is not null
        ? $"Не удалось проверить: {Error}"
        : IsUpdateAvailable
            ? $"Есть новая версия {LatestVersion} (локально {CurrentVersion})"
            : $"Актуальна ({CurrentVersion})";
}

public sealed class ExtensionsConfig
{
    /// <summary>Локальный репозиторий, куда копируются .crx и updates.xml (должен переживать удаление программы).</summary>
    public string RepositoryDirectory { get; set; } = @"%ProgramData%\SetupAssistant\Extensions";

    /// <summary>Ветки HKLM, в которые записывается политика принудительной установки (Google Chrome и Chromium, в том числе Chromium GOST).</summary>
    public List<string> PolicyRegistryKeys { get; set; } = new()
    {
        @"SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist",
        @"SOFTWARE\Policies\Chromium\ExtensionInstallForcelist"
    };

    /// <summary>Проверять новые версии расширений в фоне при запуске (без интернета проверка тихо пропускается).</summary>
    public bool CheckUpdatesOnStartup { get; set; } = true;

    public List<ExtensionDefinition> Items { get; set; } = new();
}

/// <summary>Сертификат удостоверяющего центра, импортируемый в хранилище Windows (только с проверкой отпечатка).</summary>
public sealed class CaCertificateDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    /// <summary>Ожидаемый SHA-1 отпечаток. Обязателен: при несовпадении или пустом значении сертификат не импортируется.</summary>
    public string Thumbprint { get; set; } = string.Empty;

    /// <summary>Хранилище: LocalMachine\Root, LocalMachine\CA, CurrentUser\Root или CurrentUser\CA.</summary>
    public string Store { get; set; } = @"LocalMachine\Root";
}

/// <summary>Native messaging-хост, который должен быть зарегистрирован для работы расширения браузера.</summary>
public sealed class NativeHostDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Какой компонент регистрирует хост — показывается в подсказке, если хост не найден.</summary>
    public string Description { get; set; } = string.Empty;
}

public sealed class NativeMessagingConfig
{
    /// <summary>Ветки реестра (в HKLM и HKCU), где браузеры ищут манифесты native messaging-хостов.</summary>
    public List<string> RegistryRoots { get; set; } = new()
    {
        @"SOFTWARE\Google\Chrome\NativeMessagingHosts",
        @"SOFTWARE\Chromium\NativeMessagingHosts",
        @"SOFTWARE\Microsoft\Edge\NativeMessagingHosts"
    };

    public List<NativeHostDefinition> Hosts { get; set; } = new();
}

/// <summary>Компонент, который удаляет раздел «Удаление»: находится в реестре по названию и удаляется штатным деинсталлятором.</summary>
public sealed class UninstallProductDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Части отображаемого имени в «Программах и компонентах» (первое совпадение выигрывает).</summary>
    public List<string> DisplayNamePatterns { get; set; } = new();

    /// <summary>Msi — msiexec /x по коду продукта; Exe — команда из UninstallString реестра плюс Args.</summary>
    public InstallerKind Kind { get; set; } = InstallerKind.Msi;

    /// <summary>Дополнительные ключи тихого удаления.</summary>
    public string Args { get; set; } = string.Empty;

    /// <summary>Если задано, перед удалением завершаются процессы, чей путь содержит эту подстроку (браузер, службы плагина).</summary>
    public string ProcessPathContains { get; set; } = string.Empty;

    /// <summary>
    /// Папки с настройками программы (допускаются переменные среды), которые удаляются вместе с ней после успешного удаления.
    /// Ключи, ключевые контейнеры и содержимое токена сюда не добавляются; несуществующие пути пропускаются.
    /// </summary>
    public List<string> DataPaths { get; set; } = new();

    /// <summary>
    /// Группа продукта: «Kontinent» — Континент-АП и связанные с ним компоненты (КБ CSP). Такие продукты удаляются только в
    /// режимах, где затрагивается Континент; пустое значение — обычный компонент.
    /// </summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// Ветки реестра HKLM\… / HKCU\…, которые удаляются вместе с продуктом целиком, даже если в них есть данные.
    /// Только собственные разделы продукта; общие разделы вендора (с пробелом «Security Code») сюда не добавляются.
    /// </summary>
    public List<string> RegistryPaths { get; set; } = new();
}

/// <summary>
/// Настройка Континент-АП: профиль подключения и сертификаты добавляются через консольную программу клиента
/// (VpnClientConsole.exe, ключи --addProfile, --importServerCertificate, --importRootCertificate).
/// </summary>
public sealed class KontinentConfig
{
    /// <summary>
    /// Как добавлять сертификаты: Auto — через КриптоПро, если он установлен (сертификат берётся из его контейнеров),
    /// иначе через Континент-АП; CryptoPro или Continent — принудительно.
    /// </summary>
    public string Mode { get; set; } = "Auto";

    /// <summary>Консольная программа клиента (допускаются переменные среды).</summary>
    public string ConsolePath { get; set; } = @"%ProgramFiles%\Security Code\Continent VPN Client\VpnClientConsole.exe";

    /// <summary>JSON-файл профиля подключения (--addProfile); пусто — профиль не добавляется.</summary>
    public string ProfilePath { get; set; } = string.Empty;

    /// <summary>User — профиль для текущего пользователя; Global — общий для всех пользователей (ключ -g).</summary>
    public string ProfileScope { get; set; } = "User";

    /// <summary>Файл сертификата сервера (--importServerCertificate). Только в режиме через Континент-АП.</summary>
    public string ServerCertificatePath { get; set; } = string.Empty;

    /// <summary>Файл корневого сертификата УЦ (--importRootCertificate). Только в режиме через Континент-АП.</summary>
    public string RootCertificatePath { get; set; } = string.Empty;

    /// <summary>Сколько секунд ждать каждую команду клиента.</summary>
    public int CommandTimeoutSeconds { get; set; } = 120;

    /// <summary>Программа регистрации Континент-АП (открывается только по нажатию кнопки пользователя).</summary>
    public string RegistrationProgramPath { get; set; } = @"%ProgramFiles%\Security Code\Continent VPN Client\Registration.exe";

    /// <summary>Подсказка для ручного заполнения формы регистрации: что в каком поле и в каком формате. Персональных данных здесь нет.</summary>
    public List<RegistrationFieldHint> RegistrationFields { get; set; } = new();
}

/// <summary>Одна строка подсказки: название поля, формат и пример (не данные пользователя).</summary>
public sealed record RegistrationFieldHint(string Field, string Format, string Example);

public sealed class UninstallConfig
{
    /// <summary>Порядок — обратный установке: сначала браузер и плагины, затем драйверы, последним КриптоПро CSP.</summary>
    public List<UninstallProductDefinition> Products { get; set; } = new();

    /// <summary>Остаточные папки после удаления; допускается «*» в последнем сегменте пути.</summary>
    public List<string> LeftoverPaths { get; set; } = new();

    /// <summary>Остаточные ветки реестра в формате HKCU\... или HKLM\... (ключи КриптоПро с контейнерами сюда не добавляются).</summary>
    public List<string> LeftoverRegistryKeys { get; set; } = new();

    /// <summary>Папки, которые удаляются, только если в них не осталось ни одного файла (пустые оболочки после деинсталлятора).</summary>
    public List<string> EmptyLeftoverFolders { get; set; } = new();

    /// <summary>
    /// Ветки реестра HKCU\… / HKLM\…, которые удаляются, только если в них нет ни одного значения. Подходит и для «Crypto Pro»:
    /// пустая ветка не содержит ни контейнеров, ни лицензии.
    /// </summary>
    public List<string> EmptyLeftoverRegistryKeys { get; set; } = new();

    /// <summary>Префиксы ProgID (например, «CAdESCOM.»): записи с таким именем без рабочего CLSID удаляются из HKLM\SOFTWARE\Classes.</summary>
    public List<string> OrphanProgIdPrefixes { get; set; } = new();
}

public sealed class LoggingConfig
{
    public string LogDirectory { get; set; } = "Logs";
    public string LogFileNamePattern { get; set; } = "setup-assistant-{0:yyyy-MM-dd}.log";
    public int RetainDays { get; set; } = 30;
    public string MinimumLevel { get; set; } = "Information";
}

public sealed class CheckSettingsConfig
{
    public int TokenWaitTimeoutSeconds { get; set; } = 60;
    public int TokenPollIntervalMs { get; set; } = 1500;
    public int CertificateExpiryWarningDays { get; set; } = 30;
}

public sealed class UiSettingsConfig
{
    public string Theme { get; set; } = "Light";
    public string AccentColor { get; set; } = "#2563EB";
    public bool ShowAdvancedDetails { get; set; } = true;
}

/// <summary>Описание поддерживаемого браузера — позволяет добавлять новые Chromium-совместимые браузеры без изменения логики.</summary>
public sealed class BrowserDefinition
{
    public string Name { get; set; } = string.Empty;
    public BrowserKind Kind { get; set; }
    public string RegistryDisplayNamePattern { get; set; } = string.Empty;
    public string ExecutableName { get; set; } = string.Empty;
    public bool RequiresGostSupport { get; set; }
}
