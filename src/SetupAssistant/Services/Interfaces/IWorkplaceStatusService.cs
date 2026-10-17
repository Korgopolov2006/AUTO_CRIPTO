using SetupAssistant.Models;

namespace SetupAssistant.Services.Interfaces;

/// <summary>
/// Актуальное состояние рабочего места: результаты последней диагностики, общие для главной страницы,
/// экрана диагностики и мастера. Повторные запросы во время проверки присоединяются к уже идущей.
/// </summary>
public interface IWorkplaceStatusService
{
    ReadinessSummary Summary { get; }

    bool IsChecking { get; }

    string CurrentStepText { get; }

    /// <summary>Когда закончилась последняя проверка; null — ещё не выполнялась.</summary>
    DateTime? CheckedAt { get; }

    /// <summary>Изменилось состояние проверки или её результаты (вызывается в потоке, откуда запущена проверка).</summary>
    event EventHandler? Changed;

    Task<ReadinessSummary> RefreshAsync(CancellationToken cancellationToken = default);
}
