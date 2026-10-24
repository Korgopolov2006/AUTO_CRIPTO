# Полная очистка ПК от СКЗИ: что говорит документация и что реально остаётся в системе

Исследование от 24.10.2026. Источники — официальные страницы производителей и форум КриптоПро, плюс снимок реестра и файлов
рабочего ПК (КриптоПро CSP 5.0.12000, ЭЦП Browser plug-in 2.0.15820, Драйверы Рутокен 5.3, Код Безопасности CSP,
Континент-АП, Контур.Плагин, плагин Госуслуг). Всё ниже снято только чтением, ничего не удалялось.

## 1. Что говорит документация

| Источник | Суть |
|---|---|
| [КриптоПро FAQ «Аварийное удаление КриптоПро CSP»](https://cryptopro.ru/faq/avariinoe-udalenie-kriptopro-csp) | Если штатное удаление не проходит — утилита `cspclean.exe`: запустить, перезагрузить, запустить ещё раз. **Не рекомендуется на машинах с КриптоПро УЦ или КриптоАРМ.** |
| [Форум КриптоПро, тема про cspclean](https://cryptopro.ru/forum2/default.aspx?g=posts&t=17089) | Файл: `https://www.cryptopro.ru/sites/default/files/public/cspclean.exe`, обновляется без смены имени, новые версии чистят больше продуктов и `Installer Cache`. Сотрудник предпочитает штатное удаление через msiexec; cspclean — крайняя мера. |
| [Форум КриптоПро, «Утилита cspclean»](https://cryptopro.ru/forum2/default.aspx?g=posts&t=4107) | Утилита интерактивная: спрашивает «Все продукты Крипто-Про будут уничтожены! Продолжить?» и «Удалить все ключи?». Тихого режима в документации нет. |
| [Код Безопасности, KB 4478](https://www.securitycode.ru/wiki/kontinent/4478-chistka-reestra-posle-udaleniya-kriptopro/) | Чтобы реестр был полностью очищен от следов КриптоПро, cspclean запускают с ключом `-delkeys` (удаляет и ключи). Повод — Континент-АП после переустановки КриптоПро выдаёт «Указан неправильный алгоритм». |
| [Форум КриптоПро, «Удаление Крипто-Про 5.0»](https://cryptopro.ru/forum2/default.aspx?g=posts&t=16556) | После cspclean и установки другой версии не строилась цепочка («при проверке отношений доверия произошла системная ошибка»). Эксперт советует смотреть `HKLM\SOFTWARE\Microsoft\Cryptography` и `WOW6432Node\Microsoft\Cryptography` (OID и криптопровайдеры) и сторонние CSP (ViPNet, Континент). Решилось штатным удалением + перезагрузкой. У Континента есть своя утилита с тем же именем `cspclean` — не перепутать. |
| [Saby: удаление драйверов Рутокен](https://saby.ru/help/ep/key/rutoken/delete/) | Отключить токены → удалить «Драйверы Рутокен» в «Программах и компонентах». При ошибках — официальная утилита удаления [`rtDrivers-kill`](https://www.rutoken.ru/support/download/get/rtDrivers-kill.html), затем утилита восстановления [`rtDrivers-fix`](https://www.rutoken.ru/support/download/get/rtDrivers-fix.html), перезагрузка. |
| [Хабр Q&A про ViPNet CSP](https://qna.habr.com/q/1400394) (неофициально) | У ViPNet CSP в папке установки есть `SafeModeUninstall.bat` (удаление в безопасном режиме); удаление бывает тяжёлым, советуют точку восстановления. |
| [Хабр: перенос контейнеров](https://habr.com/en/post/161361) | Контейнеры в реестре: `HKLM\SOFTWARE\(WOW6432Node\)Crypto Pro\Settings\Users\<SID>\Keys\`, машинные — `...\Settings\Keys\`. |
| Microsoft: [CryptUnregisterOIDFunction](https://learn.microsoft.com/en-us/windows/win32/api/wincrypt/nf-wincrypt-cryptunregisteroidfunction), [BCryptRemoveContextFunction](https://learn.microsoft.com/en-us/windows/win32/api/bcrypt/nf-bcrypt-bcryptremovecontextfunction), [Prioritizing Schannel Cipher Suites](https://learn.microsoft.com/en-au/windows/win32/secauthn/prioritizing-schannel-cipher-suites) | Штатные API для снятия регистраций OID и удаления шифронаборов из списка Schannel — вместо ручной правки общих разделов реестра. |

Вывод: официальной поштучной «карты реестра» нет ни у КриптоПро, ни у других. Порядок у всех один:
**штатное удаление → перезагрузка → утилита очистки → перезагрузка**. Поэтому карта ниже снята с живой системы.

## 2. Следы КриптоПро CSP 5.0 + ЭЦП Browser plug-in

Обозначения: 🟢 — раздел целиком принадлежит КриптоПро, можно удалять целиком; 🟡 — общий раздел Windows или общий с
другими ГОСТ-СКЗИ: удалять **только записи КриптоПро**, остальное не трогать. Всё, что с WOW6432Node, есть в обоих видах
реестра (64 и 32 бита).

### 2.1. Собственные ветки (🟢, но внутри ключи!)
- `HKLM\SOFTWARE\WOW6432Node\Crypto Pro` — основная на x64: `Cryptography\CurrentVersion` (KeyDevices, KeyCarriers,
  Parameters, Random, Debug, OID), **`Settings\Users\<SID>\Keys` — контейнеры закрытых ключей в реестре**,
  `Settings\Keys` — машинные контейнеры, `OCSPAPI`, `TSPAPI`.
- `HKLM\SOFTWARE\Crypto Pro`, `HKCU\SOFTWARE\Crypto Pro` (у каждого профиля).

### 2.2. CryptoAPI — `HKLM\SOFTWARE\(WOW6432Node\)Microsoft\Cryptography`
- 🟢 `Defaults\Provider\Crypto-Pro GOST R 34.10-2001 Cryptographic Service Provider`, `…2012 Cryptographic Service Provider`,
  `…2012 Strong Cryptographic Service Provider`.
- 🟡 `Defaults\Provider Types\Type 075`, `Type 080`, `Type 081` — номера типов ГОСТ общие с другими СКЗИ (ViPNet и др.):
  удалять, только если `Name` начинается с `Crypto-Pro`.
- 🟡 `Calais\SmartCards\*` — ~37 описаний носителей (`CP_*_Default`, `CP_JaCarta_*`, `CP_INPASPOT_*`, …) с
  `"Crypto Provider"="Crypto-Pro …"`. Удалять только их; записи Рутокена/Microsoft не трогать.
- 🟡 `OID\EncodingType 0|1\<функция>\<OID>` — ~110 регистраций, у которых `Dll` указывает на
  `…\Common Files\Crypto Pro\Shared\cpext.dll` (Encode/Decode, Sign/Verify, Import/Export ключей, CryptMsg*, CryptCNGInitHMAC)
  и 4 политики `CertDllVerifyCertificateChainPolicy` → `pkivalidator.dll`. **На тех же OID `1.2.643.*` сидит и Код Безопасности CSP
  (`sccsp4.dll`)**, поэтому решать по значению `Dll`, а не по номеру OID. Лучше снимать через `CryptUnregisterOIDFunction`.
- 🟡 `OID\EncodingType 0\CryptDllFindOIDInfo\1.2.643.*` — ~130 описаний ГОСТ-алгоритмов (+ `2.5.4.16!5` и др. с `CPExtraInfo`).
  Снимать через `CryptUnregisterOIDInfo`, только записи КриптоПро.
- 🟡 `OID\EncodingType 0\CryptDllFindLocalizedName\LocalizedNames` — значение `CryptoProTrustedStore`.

### 2.3. CNG и TLS — `HKLM\SYSTEM\CurrentControlSet\Control\Cryptography`
- 🟢 `Providers\Crypto-Pro Primitive Provider`.
- 🟡 `Configuration\Local\Default\0000000X\<алгоритм>` — в списках `Providers` добавлен `Crypto-Pro Primitive Provider`,
  в т. ч. в чужих (`SHA1`, `SHA256`, `00010001\KEY_STORAGE` рядом с Microsoft). Удалять **элемент списка**, не раздел;
  разделы ГОСТ-алгоритмов (`GR 34.11-2012 256`, `GOST 28147-89`, `GR 34.10-2012 512` …), где кроме КриптоПро никого нет, — целиком.
- 🟡 `Configuration\Local\Default\00010002\TLS_GOSTR341*` — 5 шифронаборов ГОСТ.
- 🟡 `Configuration\Local\SSL\00010002`, значение `Functions` — КриптоПро **дописал 5 ГОСТ-шифронаборов в общий список TLS
  Windows**. Снимать через `BCryptRemoveContextFunction(CRYPT_LOCAL, L"SSL", NCRYPT_SCHANNEL_INTERFACE, L"TLS_GOSTR…")`.

### 2.4. LSA, драйвер, перехват системных DLL — `HKLM\SYSTEM\CurrentControlSet`
- 🟡 `Control\Lsa`, `Security Packages` (REG_MULTI_SZ) содержит `cpsspap` — убрать элемент, остальное оставить; действует после перезагрузки.
- 🟢 `Services\CProCtrl` (+ `Instances`, `Parameters\CProExclude|CProIntegrity|CProPatches`) — мини-фильтр; `sc delete CProCtrl`.
- 🟢 `Control\Session Manager\CProPatches` — подмена ADVAPI32, CRYPT32, SCHANNEL, WININET, kerberos, MSI, MSO, MSXML5,
  INETCOMM, MAILCOMM, OutlMIME, EXSEC, CERTOCM на DLL из `Common Files\Crypto Pro\AppCompat`.
- 🟢 `Control\Session Manager\CProIntegrity` — хэши файлов КриптоПро.
- 🟢 `Services\EventLog\System\CProCtrl`.
- 🟡 `Services\EventLog\Application\{capi10, capi20, cloud, cng, cpcsp, cpext, cryptoki, csp, cspi, pcsc, ssp}` — имена
  общие (`cng`, `csp`, `pcsc`), удалять только если `EventMessageFile` указывает в папку `Crypto Pro` или на `cpsspap.dll`.

### 2.5. Интеграция в оболочку и COM
- 🟢 `HKLM\SOFTWARE\(WOW6432Node\)Microsoft\Windows\CurrentVersion\App Paths\{cptools.exe, cpverify.exe, csptest.exe, wipefile.exe}`.
- 🟡 `…\CurrentVersion\Control Panel\Cpls`, значение `cpconfig.cpl`; `…\Control Panel\Extended Properties\*` — значения с путём к `cpanel.cpl`.
- 🟡 `…\CurrentVersion\SharedDLLs` (и `WOW6432Node\…\SharedDlls`) — значения-счётчики с путями КриптоПро (`cpsspap.dll`,
  `cpcsp.ru.adm`, `cadescom_enrollctrl.exe`, `pkivalidator.dll`, `CadesPlugin.admx/adml`, `config.html`).
- 🟡 `HKLM\SOFTWARE\Classes` и `WOW6432Node\Classes`: ProgID `CAdESCOM.*`, CLSID/TypeLib/Interface/AppID с серверами
  `cadescom.dll`, `cplicmgmt.dll`, `pkimgmt.dll`, `pkivalidator.dll`, `ocspview.exe`, `tspview.exe`, `cadescom_enrollctrl.exe`
  (~900 разделов; MSI снимает их сам, вручную — только если сервер указывает в `Crypto Pro` и файла уже нет).

### 2.6. Браузеры и политики
- 🟢 `NativeMessagingHosts\ru.cryptopro.nmcades` в `HKLM\SOFTWARE\WOW6432Node\Google\Chrome` и `HKLM\SOFTWARE\Mozilla`
  (проверять также `Chromium`, `Microsoft\Edge`, `Yandex\YandexBrowser`, оба вида реестра, HKCU).
- 🟢 Внешние расширения КриптоПро (все три ID пускает хост `ru.cryptopro.nmcades`): `iifchhfn…` и `pfhgbfnn…` в
  `HKLM\SOFTWARE\WOW6432Node\Google\Chrome\Extensions` и `…\Microsoft\Edge\Extensions`, `epebfceh…` в
  `…\Yandex\YandexBrowser\Extensions`.
- 🟢 Политики плагина: `HKLM\SOFTWARE\Policies\Crypto-Pro\CadesPlugin` (если задавались).

### 2.7. Хранилища сертификатов
- 🟢 Системное хранилище `HKLM\SOFTWARE\Microsoft\SystemCertificates\CryptoProTrustedStore`.
- Личные сертификаты, привязанные к контейнерам КриптоПро (`My`) — программа уже умеет удалять.
- Корневые сертификаты, которые ставит `cadesplugin.exe -root` (программа ставит с `-noroot`, поэтому их нет).

### 2.8. Файлы
- 🟢 `%ProgramFiles%\Crypto Pro`, `%ProgramFiles(x86)%\Crypto Pro` (`CSP`, `CAdES Browser Plug-in`),
  `%CommonProgramFiles%\Crypto Pro` и `%CommonProgramFiles(x86)%\Crypto Pro` (`AppCompat`, `Shared`),
  `%ProgramData%\Crypto Pro` (`Installer Cache`, `Crypto`).
- 🟢 `System32` и `SysWOW64`: `cpcng.dll`, `CProDspr.<версия>.dll`, `cpssl.dll`, `cpsspap.dll`;
  `System32\drivers\CProCtrl.<версия>.sys`. **Отбирать по подписи/CompanyName «КРИПТО-ПРО», а не по маске `cp*.dll`:**
  рядом лежит `CPFilters.dll` от Microsoft.
- 🟢 `%windir%\System32\GroupPolicy\Adm\cpcsp.ru.adm`, `%windir%\PolicyDefinitions\CadesPlugin.admx`,
  `…\ru-RU\CadesPlugin.adml`, `…\en-US\CadesPlugin.adml`.
- 🟢 Меню «Пуск»: `%ProgramData%\Microsoft\Windows\Start Menu\Programs\КРИПТО-ПРО` и `…\Крипто-Про`.
- 🟢 У каждого профиля: `%LOCALAPPDATA%\Crypto Pro` (**здесь считыватель HDIMAGE хранит контейнеры — папки с `*.key`**),
  `%APPDATA%\Crypto Pro`.
- Занятые файлы — `MoveFileEx(..., MOVEFILE_DELAY_UNTIL_REBOOT)`.

### 2.9. Регистрация MSI (вручную не править)
- CSP: `Uninstall\{50F91F80-D397-437C-B0C8-62128DE3B55E}`, `Installer\UserData\S-1-5-18\Products\08F19F05793DC7340B8C2621D83E5BE5`
  (в `InstallProperties`, значение `ProductID` — серийный номер лицензии; перед полной очисткой его стоит сохранить в отчёт).
- Плагин: `Uninstall\{E12CC7EE-36B7-4AAA-924E-2F5CD75BCECF}`.
- Если штатное удаление падает из-за потерянного кэша MSI (1612) — `msiexec /x "<наш csp-x64-rus.msi>"`: у него тот же ProductCode.

## 3. Драйверы Рутокен 5.3
- `Uninstall\{511E1BAD-7DD8-48AF-B3F1-1B8FFB249211}` («Драйверы Рутокен»).
- Файлы `System32` и `SysWOW64`: `rtAPI.dll`, `rtAPIex.dll`, `rtAPIlt.dll`, `rtPKCS11ECP.dll`, по реестру ещё `rtCSP.dll`, `rtMiniDrv.dll`.
- `Microsoft\Cryptography\Defaults\Provider\Aktiv ruToken CSP v1.0` (`Image Path` = `rtCSP.dll`).
- 🟡 `Calais\SmartCards\ruToken`, `Rutoken ECP`, `Rutoken lite`, `Rutoken FKC`, `Rutoken ECP SC T0/T1`, `… BT`, `… ST23 NFC` —
  удалять, если `80000001` = `rtMiniDrv.dll` или `Crypto Provider` = `Aktiv ruToken CSP v1.0`.
- Папки: `%ProgramFiles(x86)%\Aktiv Co\RutokenControlCenter`, `%ProgramData%\Aktiv Co`.
- Аварийно: официальные `rtDrivers-kill` → `rtDrivers-fix` (ссылки в разделе 1).

## 4. Код Безопасности CSP и Континент-АП (на этом ПК)
Официальной инструкции по очистке не нашёл. Что видно в системе:
- Службы `csp4service` (`Program Files\Security Code\CSP`), `VpnService`, `IntegrityService` (`Program Files\Security Code\Continent VPN Client`), драйвер `sckcsp4`.
- `Defaults\Provider\Security Code Cryptographic Service Provider`; OID-регистрации с `Dll` = `C:\WINDOWS\system32\sccsp4.dll`.
- Системное хранилище `HKLM\SOFTWARE\Microsoft\SystemCertificates\ContinentVpnClient Server Store`.

## 5. Плагины Госуслуг и Контура
- Госуслуги: `HKCU\SOFTWARE\Google\Chrome\NativeMessagingHosts\ru.rtlabs.ifcplugin`, то же в `Mozilla` → `%APPDATA%\Rostelecom\IFCPlugin`;
  расширение `pbefkdcndngodfeigfdgiodgnmbgcfha` («Расширение для плагина Госуслуг»). `pfhgbfnn…` — не Госуслуги, а КриптоПро
  («Extension for CAdES Browser Plugin»), см. раздел 2.6.
- Контур: `kd.nc` (HKCU Chrome/Mozilla/Chromium/Edge) → `%LOCALAPPDATA%\SkbKontur\DiagPlugin`.

## 6. Что программа уже делает и чего не хватает

Уже есть (`UninstallService`, раздел `Uninstall` в `Config.json`): штатное удаление MSI/EXE в правильном порядке, папки и ветки
из `DataPaths/RegistryPaths/LeftoverPaths/LeftoverRegistryKeys`, `Installer Cache`, `kd.nc`, пустые ветки и папки, осиротевшие
ProgID `CAdESCOM.*`, удаление «зависшего» `CProCtrl` при откате, личные сертификаты КриптоПро.

Не хватает для «полной очистки» (всё это остаётся, если MSI прервали или он упал):
1. контейнеры и конфигурация КриптоПро (сейчас намеренно не трогаются);
2. общие списки Windows: LSA `Security Packages`, CNG-списки `Providers`, шифронаборы TLS в `SSL\00010002`;
3. OID-регистрации и описания ГОСТ-алгоритмов, `Defaults\Provider*`, `Calais\SmartCards\CP_*`;
4. `Session Manager\CProPatches/CProIntegrity`, источники журналов событий;
5. App Paths, Cpls, SharedDLLs, меню «Пуск», `PolicyDefinitions`, файлы в `System32/SysWOW64/drivers`;
6. `ru.cryptopro.nmcades` и внешние расширения в HKLM, `CryptoProTrustedStore`;
7. следы Рутокена, Кода Безопасности/Континента вне их папок.

## 7. Предлагаемая схема «Полной очистки»
1. **Подготовка**: права администратора; точка восстановления (`SRSetRestorePoint`/`Checkpoint-Computer`); `reg export` каждой
   удаляемой ветки и копия удаляемых папок в `Logs\cleanup-backup-<дата>`; серийный номер CSP в отчёт.
2. **Ключи**: показать найденные контейнеры (реестр по SID, HDIMAGE в профилях, машинные); резервная копия; удалять только после
   отдельного явного подтверждения. Содержимое токенов не трогать никогда.
3. **Штатное удаление** (как сейчас) → перезагрузка (у CSP всегда 3010).
4. **Глубокая очистка после перезагрузки** (через существующий `--resume`), правила:
   - 🟢-разделы и папки — целиком;
   - в 🟡-разделах — только записи, чей путь указывает в `Crypto Pro`/`Aktiv Co`/`Security Code` или чьего файла уже нет;
     никогда по маске имени;
   - списки REG_MULTI_SZ — удалить элемент, остальное сохранить; TLS — через `BCryptRemoveContextFunction`;
   - OID — через `CryptUnregisterOIDFunction`/`CryptUnregisterOIDInfo`, оба вида реестра;
   - если установлен другой ГОСТ-СКЗИ, его записи (`Type 075/080/081`, OID с его `Dll`) не трогать;
   - занятые файлы — удаление при перезагрузке; драйверы — `sc delete`;
   - пользовательские следы — для всех профилей (подгружая их `NTUSER.DAT`), не только для текущего.
5. **Вторая перезагрузка и проверка**: `certutil -csplist` без `Crypto-Pro`, нет служб/драйверов, цепочка обычного сертификата
   строится, отчёт об удалённом.

Альтернатива — встроить официальный `cspclean.exe` как шаг глубокой очистки: меньше своей логики, но утилита интерактивная,
тихий режим не документирован, состав удаляемого не опубликован, а для ПК с КриптоАРМ/УЦ её не рекомендуют.

## 8. Реализация (версия 1.7.0)

Схема из раздела 7 реализована: флажок «Полная очистка ПК от СКЗИ» на экране «Удаление» и `--uninstall --full`
(подробно — README, раздел «Полная очистка ПК от СКЗИ»). Код: `Helpers/CryptoTraceScanner.cs` (поиск, ничего не меняет),
`Helpers/CryptoTraceRemover.cs` (удаление с .reg-копией), `Helpers/KeyContainerBackup.cs`, `Services/UninstallService.FullCleanup.cs`.
Что найдёт очистка на конкретном ПК: `SetupAssistant.exe --cleanup-preview файл.txt`.

Что выяснилось при проверке на рабочем ПК и учтено в правилах:
- Описание файлов КБ CSP — «Криптопровайдер SecurityCode»: подстрока «криптопро» есть в слове «криптопровайдер». Поэтому
  производитель определяется только по полю CompanyName, а продукт — по ProductName/FileDescription.
- В папке и с подписью «Кода Безопасности» лежат и другие его продукты (Jinn-Client, Secret Net Studio), поэтому признаки
  Континента и КБ CSP — подпапки `Security Code\Continent…`, `Security Code\CSP\`, `Security Code\XC\` и названия продуктов
  в файлах (`Код Безопасности CSP`, `Континент`, `eXtendedContainer`).
- `C:\ProgramData\Crypto Pro\Crypto` закрыта даже на чтение для обычного пользователя: поиск контейнеров идёт с привилегией
  резервного копирования, а нечитаемая папка считается контейнером (без согласия не удаляется).
- Раздел `Settings\Users\<SID>` в ветке КриптоПро доступен только самому пользователю и системе, администратору — нет:
  контейнеры других пользователей читаются и удаляются с привилегиями SeBackup/SeRestore, без смены владельца.
- КриптоПро дописывает себя в общие списки CNG у SHA1, SHA256 и KEY_STORAGE (рядом с поставщиками Microsoft) — там
  снимается только поставщик (`BCryptRemoveContextFunctionProvider`), а не функция.
- Остановка драйвера `CProCtrl` на части ПК зависает в ядре, поэтому драйверы не останавливаются, а только помечаются
  на удаление (`sc delete`) и уходят при перезагрузке.

Проверка на рабочем ПК 24.10.2026 (запуск с рабочего стола от администратора):
- Очистка прошла по плану: КриптоПро завис на CProCtrl, глубокая очистка отложилась и после перезагрузки удалила 17 записей,
  драйвер и его файл ушли на удаление при загрузке. Защита системы на ПК выключена (`RPSessionInterval = 0`), поэтому точки
  восстановления нет — теперь шаг так и пишет.
- Расширения, поставленные в профиль браузера вручную (`location = 1` в Secure Preferences), остаются: браузер хранит их сам,
  а правка профиля сломала бы подпись его настроек. Очистка перечисляет их для удаления вручную.
- Приложение Claude упаковано как MSIX: программы, запущенные из его сессий, пишут AppData и HKCU в виртуальную копию
  (`AppData\Local\Packages\Claude_…\LocalCache`, `SystemAppData\Helium\User.dat`). Контур.Плагин, поставленный так 20.10,
  виден только оттуда. Следы на этом ПК проверять приложением, запущенным напрямую, а не из сессии Claude.
