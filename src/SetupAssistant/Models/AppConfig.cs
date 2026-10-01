namespace SetupAssistant.Models;

/// <summary>Корневая модель конфигурации приложения, загружаемая из Config/Config.json.</summary>
public sealed class AppConfig
{
    public string AppName { get; set; } = "Помощник настройки рабочего места для электронной подписи";
    public string AppVersion { get; set; } = "1.0.0";
    public string LogoPath { get; set; } = "Resources/logo.png";

    public InstallersConfig Installers { get; set; } = new();
    public MinVersionsConfig MinVersions { get; set; } = new();
    public RecommendedVersionsConfig RecommendedVersions { get; set; } = new();
    public RequiredComponentsConfig RequiredComponents { get; set; } = new();
    public ExtensionsConfig Extensions { get; set; } = new();
    public NativeMessagingConfig NativeMessaging { get; set; } = new();
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

    /// <summary>Ветки HKLM, в которые записывается политика принудительной установки (Chrome, Chromium, Chromium GOST).</summary>
    public List<string> PolicyRegistryKeys { get; set; } = new()
    {
        @"SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist",
        @"SOFTWARE\Policies\Chromium\ExtensionInstallForcelist",
        @"SOFTWARE\Policies\ChromiumGost\ExtensionInstallForcelist"
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

public sealed class LoggingConfig
{
    public string LogDirectory { get; set; } = "Logs";
    public string LogFileNamePattern { get; set; } = "setup-assistant-{0:yyyy-MM-dd}.log";
    public int RetainDays { get; set; } = 30;
    public string MinimumLevel { get; set; } = "Information";
}

public sealed class CheckSettingsConfig
{
    public int TokenWaitTimeoutSeconds { get; set; } = 120;
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
