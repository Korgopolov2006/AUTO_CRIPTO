using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <summary>Диалоги для режима без интерфейса (--silent / --check): ничего не показывает, подтверждает всё и пишет в журнал.</summary>
public sealed class SilentDialogService : IDialogService
{
    private readonly ILoggingService _logger;

    public SilentDialogService(ILoggingService logger)
    {
        _logger = logger;
    }

    public bool IsActionPromptVisible => false;

    public event EventHandler? ActionPromptVisibilityChanged
    {
        add { }
        remove { }
    }

    public void ShowInfo(string message, string title = "Сообщение") => _logger.Info(title, message);

    public void ShowWarning(string message, string title = "Предупреждение") => _logger.Warning(title, message);

    public void ShowError(string message, string title = "Ошибка") => _logger.Error(title, message);

    public bool Confirm(string message, string title = "Подтверждение") => true;

    // Без окна ничего не перезагружаем и не запускаем сами: выбор «отказ» оставляет решение администратору.
    public bool ConfirmChoice(string message, string title, string acceptText, string declineText)
    {
        _logger.Info(title, message.Replace("\n", " "));
        return false;
    }

    public bool ShowPreflightSummary(string title, string intro, IReadOnlyList<Models.PreflightCheck> checks,
        Func<Task<IReadOnlyList<Models.PreflightCheck>>> recheck, string continueText)
    {
        foreach (var check in checks.Where(c => c.Status != Models.StatusLevel.Ok))
        {
            _logger.Warning(title, check.ToText().Replace("\n", " "));
        }

        return !checks.Any(c => c.IsBlocking);
    }

    public IActionPrompt ShowActionPrompt(string title, string message, string? cancelText = null, TimeSpan? showAfter = null)
    {
        _logger.Info(title, message.Replace("\n", " "));
        return new NoPrompt();
    }

    public string? ShowSaveFileDialog(string defaultFileName, string filter) => null;

    private sealed class NoPrompt : IActionPrompt
    {
        public CancellationToken Cancelled => CancellationToken.None;

        public void UpdateMessage(string message)
        {
        }

        public void Dispose()
        {
        }
    }
}
