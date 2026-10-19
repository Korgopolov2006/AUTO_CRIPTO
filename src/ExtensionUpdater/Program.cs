using ExtensionUpdater;

// Инструмент сборки: код обновления лежит в src\SetupAssistant\Updater и подключён ссылкой (см. ExtensionUpdater.csproj).
// Для работы на рабочем месте то же самое делает режим --update-distributions основного exe.
return await UpdaterCli.RunAsync(args, AppContext.BaseDirectory, standalone: true);
