namespace SetupAssistant.Models;

/// <summary>
/// Описание проблемы для окна ошибки: что случилось, что делать, технические подробности отдельно.
/// </summary>
/// <param name="Title">Короткий заголовок окна («Не удалось сохранить отчёт»).</param>
/// <param name="WhatHappened">Что произошло, человеческим языком, без кодов и имён исключений.</param>
/// <param name="WhatToDo">Конкретное следующее действие; null, если сказать нечего.</param>
/// <param name="Details">Техническая причина (для поддержки): текст исключения, код возврата.</param>
/// <param name="CanRetry">В окне есть кнопка «Повторить»: вызывающий код умеет повторить действие.</param>
public sealed record ProblemInfo(
    string Title,
    string WhatHappened,
    string? WhatToDo = null,
    string? Details = null,
    bool CanRetry = false);

/// <summary>Ответ пользователя в окне ошибки.</summary>
public enum ProblemChoice
{
    Close,
    Retry
}
