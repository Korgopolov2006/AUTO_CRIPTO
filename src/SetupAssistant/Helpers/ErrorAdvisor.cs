using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using SetupAssistant.Models;

namespace SetupAssistant.Helpers;

/// <summary>
/// Превращает исключение или код возврата установщика в понятное пользователю описание проблемы.
/// Все соответствия «код → что делать» собраны здесь, а не разбросаны по проверкам текста.
/// </summary>
public static class ErrorAdvisor
{
    private const string SupportHint = "Если не помогло, нажмите «Скопировать описание» и отправьте его в поддержку: подробности также записаны в журнале.";

    /// <summary>Коды возврата Windows Installer, которые мы умеем объяснить, по убыванию приоритета.</summary>
    private static readonly (int Code, string Advice)[] InstallerCodes =
    {
        (1618, "Сейчас выполняется другая установка. Дождитесь её завершения (или перезагрузите компьютер) и нажмите «Повторить»."),
        (1603, "Установщик не смог завершиться. Закройте браузеры и программы КриптоПро, при необходимости перезагрузите компьютер и повторите. Причина записана в журнале (раздел «Журнал»)."),
        (1605, "Установщик не смог завершиться. Закройте браузеры и программы КриптоПро, при необходимости перезагрузите компьютер и повторите. Причина записана в журнале (раздел «Журнал»)."),
        (1602, "Установка была отменена. Нажмите «Повторить», когда будете готовы."),
        (1625, "Установка запрещена политикой безопасности компьютера. Обратитесь к системному администратору."),
        (1638, "На компьютере уже стоит другая версия этой программы. Удалите её в разделе «Удаление» или отметьте «Переустановить установленные» и повторите."),
        (1925, "Не хватает прав для установки. Закройте программу и запустите её заново от имени администратора."),
        (3010, "Для завершения нужна перезагрузка. Используйте «Перезагрузить и продолжить» в сводке: настройка продолжится сама."),
        (1641, "Для завершения нужна перезагрузка. Используйте «Перезагрузить и продолжить» в сводке: настройка продолжится сама.")
    };

    /// <summary>Совет по коду возврата установщика, найденному в тексте; null, если кода нет в таблице.</summary>
    public static string? AdviceForInstallerText(string text)
    {
        foreach (var (code, advice) in InstallerCodes)
        {
            if (Regex.IsMatch(text, $@"(?<![\d.]){code}(?![\d.])"))
            {
                return advice;
            }
        }

        return null;
    }

    /// <summary>Описание проблемы по исключению: <paramref name="whatFailed"/> — что именно не получилось («Не удалось сохранить отчёт»).</summary>
    public static ProblemInfo FromException(string whatFailed, Exception ex, bool canRetry = false)
    {
        var details = $"{ex.GetType().Name}: {ex.Message}";

        switch (ex)
        {
            case OperationCanceledException:
                return new ProblemInfo(whatFailed, "Действие было отменено.", "Запустите его снова, когда будете готовы.", details, canRetry);

            case UnauthorizedAccessException:
                return new ProblemInfo(whatFailed,
                    "Windows не дала доступ к файлу или папке.",
                    "Закройте программы, которые могут использовать этот файл (в том числе антивирус), и повторите. " +
                    "Если выбирали место для сохранения, выберите другую папку, например «Документы». " + SupportHint,
                    details, canRetry);

            case IOException io when IsDiskFull(io):
                return new ProblemInfo(whatFailed,
                    "На диске не хватает свободного места.",
                    "Освободите место на диске C: (нужно не менее 1 ГБ) и повторите.",
                    details, canRetry);

            case IOException:
                return new ProblemInfo(whatFailed,
                    "Не удалось прочитать или записать файл.",
                    "Проверьте, что файл или папка не открыты в другой программе, и повторите. " + SupportHint,
                    details, canRetry);

            case HttpRequestException:
            case WebException:
                return new ProblemInfo(whatFailed,
                    "Нет связи с сервером.",
                    "Проверьте подключение к интернету и повторите. Если интернета на этом компьютере нет, " +
                    "попросите администратора положить нужные файлы рядом с программой: она возьмёт их оттуда.",
                    details, canRetry);

            case Win32Exception { NativeErrorCode: 1223 }:
                return new ProblemInfo(whatFailed,
                    "Запрос на подтверждение был отклонён.",
                    "Повторите действие и в окне Windows нажмите «Да».",
                    details, canRetry);

            case Win32Exception { NativeErrorCode: 2 or 3 }:
                return new ProblemInfo(whatFailed,
                    "Не найден нужный файл или программа.",
                    "Возможно, компонент не установлен. Запустите «Диагностика» и исправьте найденные проблемы. " + SupportHint,
                    details, canRetry);

            default:
                return new ProblemInfo(whatFailed,
                    "Действие не удалось выполнить из-за непредвиденной ошибки.",
                    (canRetry ? "Нажмите «Повторить». " : "Повторите действие. ") + SupportHint,
                    details, canRetry);
        }
    }

    /// <summary>Не удалось запланировать перезагрузку: пользователю остаётся перезагрузиться самому.</summary>
    public static ProblemInfo RebootFailed(Exception ex) => new(
        "Не удалось запланировать перезагрузку",
        "Windows не приняла команду на перезагрузку.",
        "Перезагрузите компьютер сами: «Пуск» → «Завершение работы» → «Перезагрузка». После входа в систему откройте программу ещё раз и нажмите «Проверить снова».",
        $"{ex.GetType().Name}: {ex.Message}");

    private static bool IsDiskFull(IOException ex)
    {
        // ERROR_HANDLE_DISK_FULL = 39, ERROR_DISK_FULL = 112.
        var code = ex.HResult & 0xFFFF;
        return code is 39 or 112;
    }
}
