Основные дистрибутивы находятся в папке Appendices (КриптоПро CSP, Chromium GOST,
Контур.Плагин, расширения браузеров) — пути прописаны в Config/Config.json.

В эту папку при необходимости добавьте недостающие пакеты и укажите пути в Config.json:

- КриптоПро ЭЦП Browser plug-in (cadesplugin.exe) -> Installers.CryptoProBrowserPlugin.Path
- Драйверы Rutoken (msi)                          -> Installers.RutokenDriver.Path
- Драйверы JaCarta (msi)                          -> Installers.JaCartaDriver.Path

Для каждого пакета в конфигурации задаются: Path (путь), Kind (Exe|Msi) и SilentArgs
(ключи тихой установки). Жёстко прописанных путей в коде нет.
