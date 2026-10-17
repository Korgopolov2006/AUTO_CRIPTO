using System.Collections.ObjectModel;
using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Короткие неблокирующие уведомления в углу окна (вместо модальных окон для успешных действий).</summary>
public interface INotificationService
{
    ObservableCollection<ToastItem> Items { get; }

    /// <summary>Показано уведомление (в потоке интерфейса). Окно использует его, чтобы продублировать сообщение уведомлением Windows, когда не в фокусе.</summary>
    event EventHandler<ToastItem>? ToastShown;

    void Show(ToastKind kind, string title, string? message = null);

    void Dismiss(ToastItem item);
}
