namespace SetupAssistant.Models;

/// <summary>
/// Что сервер производителя отдаёт сейчас по одному дистрибутиву.
/// </summary>
/// <param name="Key">Ключ раздела Installers в Config.json («ChromiumGost»).</param>
/// <param name="Title">Название для пользователя.</param>
/// <param name="Version">Номер версии, если сервер его сообщает (GitHub-релиз, номер в имени файла); иначе null.</param>
/// <param name="FileName">Имя файла на сервере.</param>
/// <param name="Size">Размер файла на сервере, байт.</param>
/// <param name="Modified">Дата изменения файла по заголовку сервера.</param>
/// <param name="Error">Почему проверить не удалось («нет связи с сервером»); null — проверка прошла.</param>
/// <param name="IsFixed">Версия зафиксирована в Config.json (нет адреса для проверки), проверять нечего.</param>
public sealed record RemoteDistribution(
    string Key,
    string Title,
    string? Version,
    string? FileName,
    long? Size,
    DateTimeOffset? Modified,
    string? Error,
    bool IsFixed = false);
