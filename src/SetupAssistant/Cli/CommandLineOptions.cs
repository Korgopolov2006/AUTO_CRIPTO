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
    string? Error,
    IReadOnlyList<string>? UpdaterArgs = null,
    bool KontinentOnly = false,
    bool Full = false,
    bool DeleteContainers = false,
    string? CleanupPreviewPath = null)
{
    public const int ExitInvalidArguments = 4;

    /// <summary>Режим обновления дистрибутивов и расширений (бывшая отдельная утилита ExtensionUpdater).</summary>
    public const string UpdateDistributionsMode = "--update-distributions";

    /// <summary>Работа без окна: результат — код возврата, журнал и (по желанию) отчёт.</summary>
    public bool IsHeadless => Check || Silent || Uninstall || Help || SupportBundlePath is not null || Error is not null || UpdaterArgs is not null
                              || CleanupPreviewPath is not null;

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        // Остальные аргументы принадлежат обновлению (--check и т. д. там означают своё), поэтому разбирает их оно само.
        if (args.Count > 0 && args[0].Equals(UpdateDistributionsMode, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandLineOptions(false, false, false, false, false, false, false, null, null, null, args.Skip(1).ToList());
        }

        bool check = false, silent = false, uninstall = false, resume = false, reinstall = false, withToken = false, help = false, kontinentOnly = false;
        bool full = false, deleteContainers = false;
        string? report = null, supportBundle = null, error = null, cleanupPreview = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--check": check = true; break;
                case "--silent": silent = true; break;
                case "--uninstall": uninstall = true; break;
                case "--kontinent-only": kontinentOnly = true; break;
                case "--full": full = true; break;
                case "--delete-containers": deleteContainers = true; break;
                case "--cleanup-preview":
                    if (i + 1 < args.Count)
                    {
                        cleanupPreview = args[++i];
                    }
                    else
                    {
                        error = "После --cleanup-preview нужно указать путь к файлу .txt.";
                    }

                    break;
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

        if (error is null && full && (!uninstall || kontinentOnly))
        {
            error = "--full указывается вместе с --uninstall (и без --kontinent-only).";
        }

        if (error is null && deleteContainers && !full)
        {
            error = "--delete-containers указывается только вместе с --uninstall --full.";
        }

        return new CommandLineOptions(check, silent, uninstall, resume, reinstall, withToken, help, report, supportBundle, error, null, kontinentOnly,
            full, deleteContainers, cleanupPreview);
    }

    public const string Usage =
        """
        Помощник настройки рабочего места для электронной подписи

        Запуск без аргументов открывает окно приложения.

        Режимы без окна (нужны права администратора):
          --check                проверить рабочее место
          --silent               установить и настроить всё, что нужно, без вопросов
          --uninstall            удалить компоненты, установленные мастером (Chromium GOST не удаляется), без вопросов
          --kontinent-only       (с --uninstall) удалить только Континент-АП и КБ CSP; КриптоПро и плагины не трогаются
          --full                 (с --uninstall) полная очистка: ещё и все следы СКЗИ в реестре и на диске; перед этим
                                 точка восстановления и резервная копия ключевых контейнеров в папке Logs\FullCleanup-…
          --delete-containers    (с --uninstall --full) удалить и ключевые контейнеры — только после успешной резервной копии;
                                 без ключа контейнеры остаются на компьютере. Содержимое токенов не затрагивается
          --cleanup-preview <файл.txt>
                                 записать, что удалит полная очистка, ничего не меняя
          --update-distributions <папка> [--check] [--prune] [--prune-only]
                                 обновить расширения и дистрибутивы в папке приложения (нужен интернет); папка — с Config\Config.json
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
