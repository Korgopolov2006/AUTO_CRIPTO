namespace SetupAssistant.Services.Interfaces;

/// <summary>Диалоговые окна и уведомления для пользователя.</summary>
public interface IDialogService
{
    void ShowInfo(string message, string title = "Сообщение");

    void ShowWarning(string message, string title = "Предупреждение");

    void ShowError(string message, string title = "Ошибка");

    bool Confirm(string message, string title = "Подтверждение");

    /// <summary>
    /// Модальный вопрос с подписями кнопок по смыслу (например, «Перезагрузить сейчас» / «Позже»). Кнопка по умолчанию —
    /// отказ, чтобы случайный Enter не запустил необратимое действие. true — выбрано <paramref name="acceptText"/>.
    /// </summary>
    bool ConfirmChoice(string message, string title, string acceptText, string declineText);

    /// <summary>
    /// Показывает модальное окно «ожидается действие пользователя» и сразу возвращает управление: окно висит, пока
    /// вызывающий код ждёт, и закрывается через <see cref="IActionPrompt.Dispose"/>. Если задана <paramref name="cancelText"/>,
    /// в окне есть кнопка отказа — она отменяет <see cref="IActionPrompt.Cancelled"/>.
    /// </summary>
    /// <param name="showAfter">Показать окно только если ожидание затянулось (иначе оно мигало бы на быстрых операциях).</param>
    IActionPrompt ShowActionPrompt(string title, string message, string? cancelText = null, TimeSpan? showAfter = null);

    /// <summary>Сейчас на экране окно ожидания действия пользователя.</summary>
    bool IsActionPromptVisible { get; }

    event EventHandler? ActionPromptVisibilityChanged;

    /// <summary>Открывает диалог сохранения файла и возвращает выбранный путь, либо null при отмене.</summary>
    string? ShowSaveFileDialog(string defaultFileName, string filter);
}

/// <summary>Открытое окно ожидания действия пользователя. Dispose закрывает его.</summary>
public interface IActionPrompt : IDisposable
{
    /// <summary>Отменяется, когда пользователь нажал кнопку отказа в окне.</summary>
    CancellationToken Cancelled { get; }

    /// <summary>Меняет текст в уже открытом окне (например, обратный отсчёт ожидания).</summary>
    void UpdateMessage(string message);
}
