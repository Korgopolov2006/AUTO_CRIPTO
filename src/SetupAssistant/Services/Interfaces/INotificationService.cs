using System.Collections.ObjectModel;
using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>Короткие неблокирующие уведомления в углу окна (вместо модальных окон для успешных действий).</summary>
public interface INotificationService
{
    ObservableCollection<ToastItem> Items { get; }

    void Show(ToastKind kind, string title, string? message = null);

    void Dismiss(ToastItem item);
}
