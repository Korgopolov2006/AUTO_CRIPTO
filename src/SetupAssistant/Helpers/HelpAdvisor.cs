using SetupAssistant.Models;

namespace SetupAssistant.Helpers;

/// <summary>
/// Короткие советы «что делать» для результатов диагностики, шагов мастера и теста подписи.
/// Тексты написаны для пользователя, а не для администратора: без кодов и жаргона там, где это возможно.
/// </summary>
public static class HelpAdvisor
{
    /// <summary>Совет по результату проверки компонента; null, если всё в порядке и совет не нужен.</summary>
    public static string? ForComponent(ComponentInfo c)
    {
        if (c.Status is StatusLevel.Ok or StatusLevel.InProgress or StatusLevel.Skipped)
        {
            return null;
        }

        var outdated = c.Status is StatusLevel.Warning or StatusLevel.Error && !string.IsNullOrEmpty(c.InstalledVersion);

        if (c.IsDeviceManagement)
        {
            return "Компьютер не в домене и не управляется (MDM). Это нормально для домашнего или отдельного ПК, но Chromium на таком " +
                   "компьютере не применяет политику установки расширений ([BLOCKED] на странице chrome://policy). Расширения придётся " +
                   "ставить вручную, либо ввести компьютер в домен или подключить к MDM.";
        }

        return c.Type switch
        {
            ComponentType.CryptoProCsp when outdated =>
                "Версия КриптоПро CSP ниже рекомендуемой. Обновите её в разделе «Настройка» (отметьте «Переустановить установленные»).",
            ComponentType.CryptoProCsp =>
                "КриптоПро CSP нужен, чтобы подписывать документы ГОСТ-сертификатами. Установите его в разделе «Настройка». " +
                "После установки без лицензии CSP работает ограниченное время — введите серийный номер в «Панели управления КриптоПро».",
            ComponentType.CryptoProProviders =>
                "Криптопровайдеры появляются после установки КриптоПро CSP. Если CSP уже установлен, переустановите его и перезагрузите компьютер.",
            ComponentType.CryptoProBrowserPlugin when outdated =>
                "Версия КриптоПро Browser Plugin ниже рекомендуемой. Обновите её в разделе «Настройка».",
            ComponentType.CryptoProBrowserPlugin =>
                "Плагин нужен для подписи в браузере (Контур, Госуслуги, ФНС). Установите шаг «КриптоПро Browser Plugin» в разделе «Настройка».",
            ComponentType.CadesCom =>
                "Компонент CAdESCOM устанавливается вместе с КриптоПро Browser Plugin. Переустановите плагин от имени администратора и перезапустите браузер.",
            ComponentType.ChromiumGost =>
                "Chromium GOST — браузер с поддержкой ГОСТ-шифрования для сайтов, которые требуют российские сертификаты. " +
                "Нужен не на каждом компьютере: ставьте, если работаете с такими сайтами.",
            ComponentType.KonturPlugin =>
                "Контур.Плагин нужен для подписи на порталах Контур. Он ставится для текущего пользователя: после установки перезапустите браузер.",
            ComponentType.GosuslugiPlugin =>
                "Плагин нужен для входа на Госуслуги с электронной подписью. Установите его в разделе «Настройка» и перезапустите браузер.",
            ComponentType.RutokenDriver =>
                "Драйвер нужен, чтобы Windows видела токен Рутокен. После установки отключите и снова подключите токен.",
            ComponentType.JaCartaDriver =>
                "Драйвер JaCarta нужен только если вы используете токены JaCarta. Скачайте его на сайте производителя.",
            ComponentType.BrowserExtensionCryptoPro or ComponentType.BrowserExtensionKontur =>
                "Расширение подключается к браузеру политикой Chromium. На компьютере вне домена политика блокируется ([BLOCKED] в chrome://policy): " +
                "тогда расширение нужно ставить вручную или ввести компьютер в домен.",
            ComponentType.NativeMessagingHost => NativeHostAdvice(c),
            ComponentType.CryptoProReaders =>
                "Считыватели не найдены. Подключите токен в USB-порт (лучше напрямую, не через разветвитель), проверьте, что запущена служба «Смарт-карты» " +
                "(services.msc), и убедитесь, что установлен драйвер токена.",
            ComponentType.CryptoProContainers =>
                "Контейнеры закрытых ключей не найдены. Подключите носитель с ключом. Если ключ хранится в реестре или файле, " +
                "откройте «КриптоПро CSP → Сервис → Скопировать» и проверьте, что контейнер виден.",
            ComponentType.Token =>
                "Подключите токен в USB-порт. Если он подключён, но не виден, попробуйте другой порт, проверьте драйвер токена и службу «Смарт-карты».",
            ComponentType.Certificate =>
                "В хранилище есть просроченные сертификаты или сертификаты без цепочки доверия. Для подписи используйте действующий. " +
                "Если цепочка не строится, установите корневой и промежуточные сертификаты удостоверяющего центра, выдавшего сертификат.",
            ComponentType.CaCertificates =>
                "Сертификаты удостоверяющего центра не установлены. В вашей организации они обычно ставятся групповой политикой; если нет — добавьте их в конфигурацию.",
            ComponentType.KontinentAp => null,
            _ => c.Status == StatusLevel.Error ? "Подробности смотрите в журнале (раздел «Журнал») и приложите пакет для поддержки (Настройки)." : null
        };
    }

    /// <summary>Совет по хосту native messaging: называет плагин, который его регистрирует, и отличает «не установлен» от «файл потерян».</summary>
    private static string NativeHostAdvice(ComponentInfo c)
    {
        var plugin = c.Name switch
        {
            var n when n.Contains("ru.cryptopro.nmcades", StringComparison.OrdinalIgnoreCase) => "КриптоПро ЭЦП Browser plug-in",
            var n when n.Contains("kd.nc", StringComparison.OrdinalIgnoreCase) => "Контур.Плагин (Диаг.Плагин)",
            var n when n.Contains("ru.rtlabs.ifcplugin", StringComparison.OrdinalIgnoreCase) => "Плагин Госуслуг",
            _ => "соответствующий плагин"
        };

        // Запись в реестре есть, а файла нет — это уже поломка, а не «ещё не установлено».
        if (c.ErrorMessage?.Contains("манифеста", StringComparison.OrdinalIgnoreCase) == true)
        {
            return $"Запись хоста есть, но его файл удалён или повреждён. Переустановите «{plugin}» в разделе «Настройка» (отметьте «Переустановить установленные») и перезапустите браузер.";
        }

        return $"Это не ошибка: хост появляется сам, когда установлен «{plugin}», отдельно его ставить не нужно. " +
               "Если плагин уже установлен, а хост не виден, переустановите его в разделе «Настройка» и перезапустите браузер.";
    }

    /// <summary>Совет по шагу мастера или удаления, завершившемуся предупреждением или ошибкой.</summary>
    public static string? ForStep(SetupStepKind kind, StatusLevel status, string? details)
    {
        if (status is not (StatusLevel.Warning or StatusLevel.Error))
        {
            return null;
        }

        var text = details ?? string.Empty;

        if (text.Contains("1618", StringComparison.Ordinal))
        {
            return "Сейчас выполняется другая установка. Дождитесь её завершения (или перезагрузите компьютер) и нажмите «Повторить».";
        }

        if (text.Contains("1603", StringComparison.Ordinal) || text.Contains("1605", StringComparison.Ordinal))
        {
            return "Установщик не смог завершиться. Закройте браузеры и программы КриптоПро, при необходимости перезагрузите компьютер и повторите. " +
                   "Причина будет в журнале (раздел «Журнал»).";
        }

        if (text.Contains("3010", StringComparison.Ordinal) || text.Contains("перезагруз", StringComparison.OrdinalIgnoreCase))
        {
            return "Для завершения нужна перезагрузка. Используйте «Перезагрузить и продолжить» в сводке: настройка продолжится сама.";
        }

        if (text.Contains("скачать", StringComparison.OrdinalIgnoreCase) || text.Contains("загрузк", StringComparison.OrdinalIgnoreCase))
        {
            return "Нет связи с сервером загрузки. Положите дистрибутив в папку Appendices (путь указан в Config.json) — приложение использует локальный файл.";
        }

        if (text.Contains("не задан", StringComparison.OrdinalIgnoreCase) || text.Contains("не найден", StringComparison.OrdinalIgnoreCase))
        {
            return "Не найден файл дистрибутива. Проверьте папку Appendices и пути в Config.json.";
        }

        return kind switch
        {
            SetupStepKind.InstallBrowserExtensions =>
                "Политика записана, но Chromium может её не применить: на компьютере вне домена и без MDM она блокируется ([BLOCKED] на странице chrome://policy). " +
                "Варианты: ввести компьютер в домен или MDM, либо установить расширения вручную.",
            SetupStepKind.InstallTokenCertificates when text.Contains("PIN", StringComparison.OrdinalIgnoreCase) || text.Contains("ожидан", StringComparison.OrdinalIgnoreCase) =>
                "КриптоПро ждал ввода PIN-кода. Повторите шаг и введите PIN в окне КриптоПро (оно может быть за окном приложения).",
            SetupStepKind.InstallTokenCertificates =>
                "Подключите токен и повторите шаг. Если сертификат на токене есть, но не установился, откройте «КриптоПро CSP → Сервис → Просмотреть сертификаты в контейнере → Установить».",
            SetupStepKind.InstallRutokenDriver or SetupStepKind.VerifyRutokenDriver =>
                "После установки драйвера отключите и снова подключите токен. Если ошибка повторяется, установите драйвер вручную из папки Appendices.",
            SetupStepKind.InstallCryptoProCsp or SetupStepKind.VerifyCryptoProCsp =>
                "Закройте все программы КриптоПро и браузеры, перезагрузите компьютер и повторите установку.",
            SetupStepKind.InstallChromiumGost or SetupStepKind.VerifyChromiumGost =>
                "Закройте Chromium GOST и повторите установку. Если ошибка осталась, смотрите журнал.",
            SetupStepKind.InstallKonturPlugin or SetupStepKind.VerifyKonturPlugin or SetupStepKind.InstallGosuslugiPlugin or SetupStepKind.VerifyGosuslugiPlugin =>
                "Закройте браузеры и повторите установку плагина от имени администратора.",
            _ => status == StatusLevel.Error ? "Нажмите «Повторить». Если ошибка повторяется, посмотрите журнал или соберите пакет для поддержки (Настройки)." : null
        };
    }

    /// <summary>Совет по этапу теста подписи, завершившемуся ошибкой.</summary>
    public static string? ForSignatureStage(string stage, string error)
    {
        var text = error;

        if (text.Contains("0x8010006E", StringComparison.OrdinalIgnoreCase) || text.Contains("отмен", StringComparison.OrdinalIgnoreCase))
        {
            return "Операция отменена: PIN-код не был введён. Повторите тест и введите PIN-код токена в окне КриптоПро.";
        }

        if (text.Contains("0x8010006B", StringComparison.OrdinalIgnoreCase) || text.Contains("0x80100069", StringComparison.OrdinalIgnoreCase)
            || text.Contains("PIN", StringComparison.OrdinalIgnoreCase))
        {
            return "PIN-код неверный или токен заблокирован. Проверьте PIN; при блокировке нужен PIN администратора (раздел «PIN-код носителя»).";
        }

        if (text.Contains("0x80090016", StringComparison.OrdinalIgnoreCase) || text.Contains("keyset", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ключев", StringComparison.OrdinalIgnoreCase))
        {
            return "Закрытый ключ недоступен. Подключите носитель, на котором создан этот сертификат, и повторите тест.";
        }

        if (text.Contains("0x800B010A", StringComparison.OrdinalIgnoreCase) || text.Contains("цепоч", StringComparison.OrdinalIgnoreCase)
            || text.Contains("доверия", StringComparison.OrdinalIgnoreCase))
        {
            return "Не удалось построить цепочку доверия. Установите корневой и промежуточные сертификаты удостоверяющего центра.";
        }

        if (text.Contains("0x800B0101", StringComparison.OrdinalIgnoreCase) || text.Contains("истёк", StringComparison.OrdinalIgnoreCase))
        {
            return "Срок действия сертификата истёк. Подпишите другим, действующим сертификатом или перевыпустите этот.";
        }

        return stage switch
        {
            "component" => "Установите КриптоПро Browser Plugin в разделе «Настройка» и повторите тест.",
            "certificate" => "Выберите действующий сертификат с закрытым ключом.",
            _ => "Повторите тест. Если ошибка осталась, соберите пакет для поддержки (Настройки → «Пакет для поддержки») и отправьте разработчику."
        };
    }
}
