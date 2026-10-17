namespace SetupAssistant.Models;

/// <summary>Правила, общие для всех проверок: группа в диагностике, чем исправить проблему, обязателен ли компонент.</summary>
public static class ComponentCatalog
{
    public const string CryptoPro = "КриптоПро";
    public const string Browsers = "Браузеры";
    public const string Plugins = "Плагины и расширения";
    public const string TokenAndCerts = "Носитель и сертификаты";
    public const string System = "Система";

    /// <summary>Заполняет Category / FixStep / IsOptional по типу компонента.</summary>
    public static ComponentInfo Classify(ComponentInfo info, AppConfig config)
    {
        // Проверка управления компьютером помечена типом расширения — выделяем её по имени.
        if (info.Type == ComponentType.BrowserExtensionCryptoPro && info.Name.StartsWith("Управление компьютером", StringComparison.Ordinal))
        {
            info.Category = System;
            return info;
        }

        (info.Category, info.FixStep) = info.Type switch
        {
            ComponentType.CryptoProCsp => (CryptoPro, SetupStepKind.InstallCryptoProCsp),
            ComponentType.CryptoProProviders => (CryptoPro, (SetupStepKind?)null),
            ComponentType.CryptoProBrowserPlugin => (CryptoPro, SetupStepKind.InstallBrowserPlugin),
            ComponentType.CadesCom => (CryptoPro, SetupStepKind.InstallBrowserPlugin),
            ComponentType.ChromiumGost => (Browsers, SetupStepKind.InstallChromiumGost),
            ComponentType.KonturPlugin => (Plugins, SetupStepKind.InstallKonturPlugin),
            ComponentType.GosuslugiPlugin => (Plugins, SetupStepKind.InstallGosuslugiPlugin),
            ComponentType.BrowserExtensionCryptoPro or ComponentType.BrowserExtensionKontur => (Plugins, SetupStepKind.InstallBrowserExtensions),
            ComponentType.NativeMessagingHost => (Plugins, null),
            ComponentType.RutokenDriver => (TokenAndCerts, SetupStepKind.InstallRutokenDriver),
            ComponentType.JaCartaDriver => (TokenAndCerts, null),
            ComponentType.CryptoProReaders or ComponentType.CryptoProContainers or ComponentType.Token => (TokenAndCerts, null),
            // Сертификаты: просрочка и отсутствие цепочки доверия установкой с носителя не лечатся, поэтому автоматического исправления нет.
            ComponentType.Certificate => (TokenAndCerts, null),
            ComponentType.CaCertificates => (TokenAndCerts, SetupStepKind.InstallCaCertificates),
            ComponentType.KontinentAp => (System, null),
            _ => ("Прочее", null)
        };

        info.IsOptional = info.Type switch
        {
            ComponentType.KontinentAp => true,
            ComponentType.JaCartaDriver => !config.RequiredComponents.JaCartaDriver,
            _ => false
        };

        return info;
    }

    /// <summary>Порядок групп в списке диагностики.</summary>
    public static int CategoryOrder(string category) => category switch
    {
        CryptoPro => 0,
        Browsers => 1,
        Plugins => 2,
        TokenAndCerts => 3,
        System => 4,
        _ => 5
    };
}
