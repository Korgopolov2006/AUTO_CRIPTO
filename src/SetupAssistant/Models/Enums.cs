namespace SetupAssistant.Models;

/// <summary>Итоговый статус проверки компонента или этапа диагностики.</summary>
public enum StatusLevel
{
    NotChecked,
    InProgress,
    Ok,
    Warning,
    Error
}

/// <summary>Тип компонента рабочего места, участвующего в диагностике/установке.</summary>
public enum ComponentType
{
    CryptoProCsp,
    CryptoProBrowserPlugin,
    ChromiumGost,
    KonturPlugin,
    BrowserExtensionCryptoPro,
    BrowserExtensionKontur,
    RutokenDriver,
    JaCartaDriver,
    GosuslugiPlugin,
    NativeMessagingHost,
    CadesCom,
    CryptoProProviders,
    CryptoProContainers,
    CryptoProReaders,
    KontinentAp,
    CaCertificates,
    Token,
    Certificate
}

/// <summary>Поддерживаемый тип носителя электронной подписи.</summary>
public enum TokenType
{
    Unknown,
    Rutoken,
    JaCarta,
    FileContainer
}

/// <summary>Тип поддерживаемого браузера (для расширяемой поддержки Chromium-подобных браузеров).</summary>
public enum BrowserKind
{
    ChromiumGost,
    Chrome,
    Yandex,
    Edge
}

/// <summary>Формат экспортируемого отчета.</summary>
public enum ReportFormat
{
    Txt,
    Pdf
}

/// <summary>Этап мастера настройки рабочего места.</summary>
public enum SetupStepKind
{
    InstallCryptoProCsp,
    VerifyCryptoProCsp,
    InstallBrowserPlugin,
    VerifyBrowserPlugin,
    InstallRutokenDriver,
    VerifyRutokenDriver,
    InstallChromiumGost,
    VerifyChromiumGost,
    InstallKonturPlugin,
    VerifyKonturPlugin,
    InstallGosuslugiPlugin,
    VerifyGosuslugiPlugin,
    InstallBrowserExtensions,
    InstallCaCertificates,
    InstallTokenCertificates,
    FinalDiagnostic
}
