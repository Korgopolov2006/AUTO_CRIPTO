# Помощник настройки рабочего места для электронной подписи

WPF (.NET 8, MVVM, DI) — автономное приложение для диагностики и настройки рабочего места для работы с электронной подписью.

## Сборка и запуск

```
dotnet build src/SetupAssistant/SetupAssistant.csproj
dotnet run --project src/SetupAssistant/SetupAssistant.csproj
```

## Публикация (self-contained, win-x64, один exe)

```
dotnet publish src/SetupAssistant/SetupAssistant.csproj -c Release
```

Результат: `src/SetupAssistant/bin/Release/net8.0-windows/win-x64/publish/SetupAssistant.exe`
(запускается с запросом прав администратора — задано в `App_Manifest/app.manifest`).

## Конфигурация

Все пути к установщикам, минимальные/рекомендуемые версии ПО и параметры UI — в
`src/SetupAssistant/Config/Config.json` (поддерживает комментарии).

## Дистрибутивы (папка Appendices)

Реальные дистрибутивы лежат в `src/SetupAssistant/Appendices/` и устанавливаются тихо:

| Компонент | Файл | Способ тихой установки |
|---|---|---|
| КриптоПро CSP 5.0.12000 | `Крипто Про Windows\csp-x64-rus.msi` | `msiexec /qn /norestart` |
| Chromium GOST 148 | `chromium-gost-148...-installer.exe` | mini_installer: `--system-level --do-not-launch-chrome` |
| Контур.Плагин | `DiagPlugin_user.002726.exe` | NSIS: `/S` |
| Расширения (5 шт.) | `расширения chromium\**\*.crx` | политика ExtensionInstallForcelist + локальный `updates.xml` |

Расширения ставятся офлайн: `.crx` копируются в `%ProgramData%\SetupAssistant\Extensions`,
рядом генерируется `updates.xml`, ID вносятся в форс-лист политик Chrome / Chromium / Chromium GOST.
ID каждого расширения вычислен из его `.pem`-ключа; при переупаковке `.crx` очистите поле `Id`
в Config.json — оно будет вычислено заново автоматически.

Дистрибутив КриптоПро ЭЦП Browser plug-in (`cadesplugin.exe`) в Appendices отсутствует —
соответствующий шаг мастера помечается «пропущен», пока файл не добавлен и путь не указан
в `Installers.CryptoProBrowserPlugin.Path`. Драйверы Rutoken/JaCarta — аналогично
(кладутся в `Installers/`, пути прописываются в конфигурации).

## Архитектура

- `Models` — доменные модели (компоненты, сертификаты, конфигурация, журнал, отчёт).
- `Services/Interfaces` + `Services` — по одному сервису на область ответственности
  (реестр, система, установка, КриптоПро, браузеры, расширения, токены, сертификаты,
  диагностика, отчёты, логирование, конфигурация, навигация, диалоги).
- `ViewModels` — MVVM (CommunityToolkit.Mvvm), навигация через `INavigationService`.
- `Views` — экраны: главный, диагностика, мастер настройки, сертификаты, журнал, настройки.
- `App.xaml.cs` — регистрация зависимостей через `Microsoft.Extensions.DependencyInjection`.
