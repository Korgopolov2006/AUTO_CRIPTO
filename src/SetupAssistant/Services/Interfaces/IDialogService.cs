namespace SetupAssistant.Services.Interfaces;

/// <summary>Диалоговые окна и уведомления для пользователя.</summary>
public interface IDialogService
{
    void ShowInfo(string message, string title = "Сообщение");

    void ShowWarning(string message, string title = "Предупреждение");

    void ShowError(string message, string title = "Ошибка");

    bool Confirm(string message, string title = "Подтверждение");

    /// <summary>Открывает диалог сохранения файла и возвращает выбранный путь, либо null при отмене.</summary>
    string? ShowSaveFileDialog(string defaultFileName, string filter);
}
