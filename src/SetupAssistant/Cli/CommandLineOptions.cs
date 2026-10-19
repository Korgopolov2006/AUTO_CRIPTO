namespace SetupAssistant.Cli;

/// <summary>Аргументы командной строки: режимы без интерфейса для администраторов (GPO, SCCM) и продолжение после перезагрузки.</summary>
public sealed record CommandLineOptions(
    bool Check,
    bool Silent,
    bool Uninstall,
    bool Resume,
    bool Reinstall,
    bool WithToken,
    bool Help,
    string? ReportPath,
    string? SupportBundlePath,
    string? Error)
{
    public const int ExitInvalidArguments = 4;

    /// <summary>Работа без окна: результат — код возврата, журнал и (по желанию) отчёт.</summary>
    public bool IsHeadless => Check || Silent || Uninstall || Help || SupportBundlePath is not null || Error is not null;

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        bool check = false, silent = false, uninstall = false, resume = false, reinstall = false, withToken = false, help = false;
        string? report = null, supportBundle = null, error = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--check": check = true; break;
                case "--silent": silent = true; break;
                case "--uninstall": uninstall = true; break;
                case "--resume": resume = true; break;
                case "--reinstall": reinstall = true; break;
                case "--with-token": withToken = true; break;
                case "--help" or "-h" or "/?": help = true; break;
                case "--support-bundle":
                    if (i + 1 < args.Count)
                    {
                        supportBundle = args[++i];
                    }
                    else
                    {
                        error = "После --support-bundle нужно указать путь к файлу .zip.";
                    }

                    break;
                case "--report":
                    if (i + 1 < args.Count)
                    {
                        report = args[++i];
                    }
                    else
                    {
                        error = "После --report нужно указать путь к файлу (.pdf или .txt).";
                    }

                    break;
                default:
                    error ??= $"Неизвестный аргумент: {args[i]}";
                    break;
            }
        }

        if (error is null && (check ? 1 : 0) + (silent ? 1 : 0) + (uninstall ? 1 : 0) > 1)
        {
            error = "Режимы --check, --silent и --uninstall нельзя указывать одновременно.";
        }

        return new CommandLineOptions(check, silent, uninstall, resume, reinstall, withToken, help, report, supportBundle, error);
    }

    public const string Usage =
        """
        Помощник настройки рабочего места для электронной подписи

        Запуск без аргументов открывает окно приложения.

        Режимы без окна (нужны права администратора):
          --check                проверить рабочее место
          --silent               установить и настроить всё, что нужно, без вопросов
          --uninstall            удалить компоненты, установленные мастером (Chromium GOST не удаляется), без вопросов
        Дополнительно:
          --report <файл>        сохранить отчёт (.pdf или .txt)
          --support-bundle <zip> собрать пакет для поддержки (журналы, диагностика) без окна
          --reinstall            (--silent) переустановить и уже установленные компоненты
          --with-token           (--silent) выполнить и шаг установки сертификата с носителя
          --resume               продолжить настройку после перезагрузки (запускает окно)
          --help                 эта справка

        Коды возврата:
          0  всё в порядке            1  есть предупреждения
          2  есть ошибки              3  установка невозможна (предварительная проверка)
          4  неверные аргументы
        """;
}
