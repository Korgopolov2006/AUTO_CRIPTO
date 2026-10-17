using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="INotificationService"/>
public sealed class NotificationService : INotificationService
{
    private const int MaxVisible = 4;

    public ObservableCollection<ToastItem> Items { get; } = new();

    public void Show(ToastKind kind, string title, string? message = null)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => Show(kind, title, message));
            return;
        }

        var item = new ToastItem { Kind = kind, Title = title, Message = message };
        Items.Add(item);
        while (Items.Count > MaxVisible)
        {
            Items.RemoveAt(0);
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind is ToastKind.Warning or ToastKind.Error ? 8 : 5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Dismiss(item);
        };
        timer.Start();
    }

    public void Dismiss(ToastItem item) => Items.Remove(item);
}
