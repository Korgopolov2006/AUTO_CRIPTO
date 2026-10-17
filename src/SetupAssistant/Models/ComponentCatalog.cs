namespace SetupAssistant.Models;

/// <summary>Правила, общие для всех проверок: группа в диагностике, чем исправить проблему, что считать сведениями, а не проблемой.</summary>
public static class ComponentCatalog
{
    public const string CryptoPro = "КриптоПро";
    public const string Browsers = "Браузеры";
    public const string Plugins = "Плагины и расширения";
    public const string TokenAndCerts = "Носитель и сертификаты";
    public const string System = "Система";

    /// <summary>Заполняет Category / FixStep / IsOptional / статус «К сведению» по типу компонента.</summary>
    public static ComponentInfo Classify(ComponentInfo info, AppConfig config)
    {
        // Проверка управления компьютером помечена типом расширения — выделяем её по имени.
        if (info.Type == ComponentType.BrowserExtensionCryptoPro && info.IsDeviceManagement)
        {
            info.Category = System;
            info.ShowOnDashboard = true;
            info.InfoText = "Не в домене";
            if (info.Status is StatusLevel.Warning or StatusLevel.Error)
            {
                info.Status = StatusLevel.Info;
            }

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
            ComponentType.Certificate => (TokenAndCerts, null),
            ComponentType.CaCertificates => (TokenAndCerts, SetupStepKind.InstallCaCertificates),
            ComponentType.KontinentAp => (System, null),
            _ => ("Прочее", null)
        };

        // Сторонний браузер (Chrome, Edge, Яндекс) мастером не устанавливается: исправление только для Chromium GOST.
        if (info.Type == ComponentType.ChromiumGost && !info.Description.StartsWith("Браузер с поддержкой ГОСТ", StringComparison.Ordinal))
        {
            info.FixStep = null;
        }

        ConvertToInformation(info);

        info.ShowOnDashboard = info.Type switch
        {
            ComponentType.CryptoProCsp or ComponentType.CryptoProBrowserPlugin or ComponentType.KonturPlugin
                or ComponentType.GosuslugiPlugin or ComponentType.RutokenDriver or ComponentType.Token => true,
            ComponentType.ChromiumGost => info.FixStep is not null,
            _ => false
        };

        info.IsOptional = info.Type switch
        {
            ComponentType.KontinentAp => true,
            ComponentType.JaCartaDriver => !config.RequiredComponents.JaCartaDriver,
            _ => false
        };

        return info;
    }

    /// <summary>
    /// Не на каждом компьютере нужен весь набор: «не установлено», «не подключён», «не найдено» — это сведения, а не ошибки.
    /// Предупреждением остаётся установленный компонент с версией ниже допустимой.
    /// </summary>
    private static void ConvertToInformation(ComponentInfo info)
    {
        var notOk = info.Status is StatusLevel.Warning or StatusLevel.Error;

        switch (info.Type)
        {
            case ComponentType.CryptoProCsp:
            case ComponentType.CryptoProBrowserPlugin:
            case ComponentType.ChromiumGost:
            case ComponentType.KonturPlugin:
            case ComponentType.GosuslugiPlugin:
            case ComponentType.RutokenDriver:
            case ComponentType.JaCartaDriver:
            case ComponentType.CadesCom:
                if ((notOk && string.IsNullOrEmpty(info.InstalledVersion)) || info.Status == StatusLevel.NotChecked)
                {
                    info.Status = StatusLevel.Info;
                    info.InfoText = "Не установлено";
                }
                else if (info.Status == StatusLevel.Error)
                {
                    // Компонент установлен, но версия ниже допустимой: это уже повод обновить, но не ошибка запуска.
                    info.Status = StatusLevel.Warning;
                }

                break;

            case ComponentType.BrowserExtensionCryptoPro:
            case ComponentType.BrowserExtensionKontur:
            case ComponentType.CaCertificates:
                SetInfo(info, notOk, "Не установлено");
                break;

            case ComponentType.NativeMessagingHost:
                SetInfo(info, notOk, "Не зарегистрировано");
                break;

            case ComponentType.CryptoProProviders:
            case ComponentType.CryptoProReaders:
            case ComponentType.CryptoProContainers:
                SetInfo(info, notOk, "Не обнаружено");
                break;

            case ComponentType.KontinentAp:
                SetInfo(info, notOk || info.Status == StatusLevel.NotChecked, "Не установлено");
                break;

            case ComponentType.Token:
                SetInfo(info, notOk, "Не подключён");
                break;

            case ComponentType.Certificate:
                // В хранилище почти всегда лежат старые сертификаты: это сведения о них, а не неисправность компьютера.
                SetInfo(info, notOk, "Есть замечания");
                break;
        }
    }

    private static void SetInfo(ComponentInfo info, bool condition, string text)
    {
        if (condition)
        {
            info.Status = StatusLevel.Info;
            info.InfoText = text;
        }
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
