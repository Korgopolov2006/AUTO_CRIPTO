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

Реальные дистрибутивы лежат в `src/SetupAssistant/Appendices/` и устанавливаются тихо. Папка исключена из git
(репозиторий публичный: лицензионное ПО и приватные ключи `.pem` расширений не публикуются) — после клонирования
разложите файлы по путям из `Config/Config.json`:

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

Обновление расширений: приложение при запуске (если есть интернет) сверяет версии с Chrome Web Store
и показывает уведомление на главном экране; то же по кнопке в «Настройках». Скачать новые `.crx` и
обновить `Config.json` можно утилитой `ExtensionUpdater.exe` на компьютере с интернетом: положите её
рядом с `SetupAssistant.exe` и запустите (или укажите папку приложения аргументом; `--check` — только
проверить). Перед правкой создаётся `Config.json.bak`. У расширений из магазина ID равен `StoreId`.
Старые версии `.crx` утилита показывает и удаляет по ключу `--prune` (при обычном запуске двойным
щелчком спросит подтверждение); файл из `Config.json` и `.pem` не удаляются. Сборка: `dotnet publish src/ExtensionUpdater/ExtensionUpdater.csproj -c Release`
(результат — `src/ExtensionUpdater/bin/Release/net8.0/win-x64/publish/ExtensionUpdater.exe`, около 34 МБ).

Дистрибутив КриптоПро ЭЦП Browser plug-in (`cadesplugin.exe`) в Appendices отсутствует —
соответствующий шаг мастера помечается «пропущен», пока файл не добавлен и путь не указан
в `Installers.CryptoProBrowserPlugin.Path`. Драйверы Rutoken/JaCarta — аналогично
(кладутся в `Installers/`, пути прописываются в конфигурации).

## Что проверяет и настраивает мастер

Шаги: КриптоПро CSP → Browser Plugin → драйвер Рутокен → Chromium GOST → Контур.Плагин → Плагин Госуслуг →
расширения браузера → сертификаты УЦ → сертификат с токена в хранилище → итоговая диагностика.

- **Драйвер Рутокен**: `Appendices\Рутокен\rtDrivers.x64.msi` (5.3.00.0000, скачан с rutoken.ru).
  **КриптоПро ЭЦП Browser plug-in**: файл `cadesplugin.exe` с cryptopro.ru (нужен вход в аккаунт),
  путь — `Installers.CryptoProBrowserPlugin`; пока не добавлен, шаг пропускается.
- **Сертификаты УЦ** (`CaCertificates`): по умолчанию список пуст (сертификаты ставятся политиками), шаг и
  проверка скрыты. Если заполнить — импорт только при совпадении отпечатка, действующем сроке и признаке УЦ.
- **Сертификат с токена**: после подключения носителя запускается `csptest -absorb -certs -autoprov`.
- **Диагностика** дополнительно показывает: провайдеры КриптоПро (типы 80/81/75), ключевые контейнеры и
  считыватели (`csptest`), native messaging-хосты расширений (`NativeMessaging`), COM CAdESCOM,
  наличие «Континент-АП» (только определение), цепочку доверия, Key Usage, EKU и политики сертификатов.
- Намеренно не реализовано: подавление лицензионных запросов КриптоПро, исключения CSP, включение старых
  шифров, «доверенные узлы» плагина и ручная регистрация OID.

## Архитектура

- `Models` — доменные модели (компоненты, сертификаты, конфигурация, журнал, отчёт).
- `Services/Interfaces` + `Services` — по одному сервису на область ответственности
  (реестр, система, установка, КриптоПро, браузеры, расширения, токены, сертификаты,
  диагностика, отчёты, логирование, конфигурация, навигация, диалоги).
- `ViewModels` — MVVM (CommunityToolkit.Mvvm), навигация через `INavigationService`.
- `Views` — экраны: главный, диагностика, мастер настройки, сертификаты, журнал, настройки.
- `App.xaml.cs` — регистрация зависимостей через `Microsoft.Extensions.DependencyInjection`.
