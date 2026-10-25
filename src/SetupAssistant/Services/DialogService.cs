using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using SetupAssistant.Helpers;
using ModernWpf.Controls;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IDialogService"/>
/// <remarks>
/// Сообщения показываются модальными <see cref="ContentDialog"/> ModernWpf — в стиле и теме приложения.
/// Интерфейс синхронный (вызывается из команд ViewModel), поэтому диалог ждёт ответа во вложенном цикле
/// сообщений: окно остаётся отзывчивым, как и при системном MessageBox.
/// </remarks>
public sealed class DialogService : IDialogService
{
    private enum DialogKind
    {
        Info,
        Warning,
        Error,
        Question
    }

    public void ShowInfo(string message, string title = "Сообщение") =>
        Show(DialogKind.Info, title, message, "ОК", null);

    public void ShowWarning(string message, string title = "Предупреждение") =>
        Show(DialogKind.Warning, title, message, "ОК", null);

    public void ShowError(string message, string title = "Ошибка") =>
        Show(DialogKind.Error, title, message, "ОК", null);

    public bool Confirm(string message, string title = "Подтверждение") =>
        Show(DialogKind.Question, title, message, "Да", "Нет") == ContentDialogResult.Primary;

    public bool ConfirmChoice(string message, string title, string acceptText, string declineText) =>
        Show(DialogKind.Warning, title, message, acceptText, declineText, ContentDialogButton.Close) == ContentDialogResult.Primary;

    private static ContentDialogResult Show(
        DialogKind kind,
        string title,
        string message,
        string primaryText,
        string? closeText,
        ContentDialogButton defaultButton = ContentDialogButton.Primary)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            return dispatcher.Invoke(() => Show(kind, title, message, primaryText, closeText, defaultButton));
        }

        var dialog = new ContentDialog
        {
            Title = BuildTitle(kind, title),
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText ?? string.Empty,
            DefaultButton = defaultButton
        };

        // Единственная кнопка («ОК») закрывает окно как основная, при подтверждении «Нет» — кнопка закрытия.
        var result = ContentDialogResult.None;
        var frame = new DispatcherFrame();
        dialog.Closed += (_, args) =>
        {
            result = args.Result;
            frame.Continue = false;
        };

        _ = dialog.ShowAsync();
        Dispatcher.PushFrame(frame);
        return result;
    }

    private static object BuildTitle(DialogKind kind, string title)
    {
        var (glyph, brushKey) = kind switch
        {
            DialogKind.Warning => ("", "StatusWarningBrush"),
            DialogKind.Error => ("", "StatusErrorBrush"),
            DialogKind.Question => ("", "SystemControlHighlightAccentBrush"),
            _ => ("", "SystemControlHighlightAccentBrush")
        };

        var icon = new FontIcon { Glyph = glyph, FontSize = 22, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(FontIcon.ForegroundProperty, brushKey);

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    public bool IsActionPromptVisible { get; private set; }

    public event EventHandler? ActionPromptVisibilityChanged;

    public IActionPrompt ShowActionPrompt(string title, string message, string? cancelText = null, TimeSpan? showAfter = null)
    {
        var dispatcher = Application.Current.Dispatcher;
        return dispatcher.CheckAccess()
            ? new ActionPrompt(this, title, message, cancelText, showAfter)
            : dispatcher.Invoke(() => new ActionPrompt(this, title, message, cancelText, showAfter));
    }

    private void SetPromptVisible(bool visible)
    {
        if (IsActionPromptVisible == visible)
        {
            return;
        }

        IsActionPromptVisible = visible;
        ActionPromptVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public string? ShowSaveFileDialog(string defaultFileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            FileName = defaultFileName,
            Filter = filter
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>Окно ожидания: индикатор, пояснение и (по желанию) кнопка отказа. Живёт, пока не вызван Dispose.</summary>
    private sealed class ActionPrompt : IActionPrompt
    {
        private readonly DialogService _owner;
        private readonly CancellationTokenSource _cancelled = new();
        private readonly ContentDialog _dialog;
        private readonly DispatcherTimer? _delayTimer;
        private readonly TextBlock _messageText;
        private bool _disposed;
        private bool _shown;

        public ActionPrompt(DialogService owner, string title, string message, string? cancelText, TimeSpan? showAfter)
        {
            _owner = owner;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new ProgressRing { IsActive = true, Width = 32, Height = 32, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top });
            _messageText = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(_messageText);

            _dialog = new ContentDialog
            {
                Title = BuildTitle(DialogKind.Info, title),
                Content = content,
                CloseButtonText = cancelText ?? string.Empty
            };

            // Закрыли сами (кнопка отказа или Esc), а не через Dispose — это отказ пользователя.
            _dialog.Closed += (_, _) =>
            {
                if (!_disposed)
                {
                    _cancelled.CancelInBackground();
                    _owner.SetPromptVisible(false);
                }
            };

            if (showAfter is { } delay && delay > TimeSpan.Zero)
            {
                _delayTimer = new DispatcherTimer { Interval = delay };
                _delayTimer.Tick += (_, _) =>
                {
                    _delayTimer.Stop();
                    ShowNow();
                };
                _delayTimer.Start();
            }
            else
            {
                ShowNow();
            }
        }

        public CancellationToken Cancelled => _cancelled.Token;

        public void UpdateMessage(string message)
        {
            var dispatcher = Application.Current.Dispatcher;
            if (dispatcher.CheckAccess())
            {
                _messageText.Text = message;
            }
            else
            {
                dispatcher.BeginInvoke(() => _messageText.Text = message);
            }
        }

        private void ShowNow()
        {
            if (_disposed || _shown)
            {
                return;
            }

            _shown = true;
            _owner.SetPromptVisible(true);

            // Окно приложения может быть свёрнуто или закрыто другим: привлекаем внимание миганием на панели задач.
            WindowFlasher.FlashUntilActivated(Application.Current.MainWindow);
            _ = _dialog.ShowAsync();
        }

        public void Dispose()
        {
            var dispatcher = Application.Current.Dispatcher;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(Dispose);
                return;
            }

            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _delayTimer?.Stop();
            if (_shown)
            {
                _dialog.Hide();
                _owner.SetPromptVisible(false);
            }
        }
    }
}
