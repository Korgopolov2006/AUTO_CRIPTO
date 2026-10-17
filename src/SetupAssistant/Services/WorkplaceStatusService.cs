using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IWorkplaceStatusService"/>
public sealed class WorkplaceStatusService : IWorkplaceStatusService
{
    private readonly IDiagnosticService _diagnosticService;
    private readonly ILoggingService _logger;
    private Task<ReadinessSummary>? _running;

    public WorkplaceStatusService(IDiagnosticService diagnosticService, ILoggingService logger)
    {
        _diagnosticService = diagnosticService;
        _logger = logger;
    }

    public ReadinessSummary Summary { get; private set; } = ReadinessSummary.Empty;

    public bool IsChecking => _running is not null;

    public string CurrentStepText { get; private set; } = string.Empty;

    public DateTime? CheckedAt { get; private set; }

    public event EventHandler? Changed;

    public Task<ReadinessSummary> RefreshAsync(CancellationToken cancellationToken = default)
    {
        // Вызывается из UI-потока: повторный запрос во время проверки получает ту же задачу.
        return _running ??= RunAsync(cancellationToken);
    }

    private async Task<ReadinessSummary> RunAsync(CancellationToken cancellationToken)
    {
        CurrentStepText = "Подготовка…";
        Changed?.Invoke(this, EventArgs.Empty);

        var progress = new Progress<string>(text =>
        {
            CurrentStepText = text;
            Changed?.Invoke(this, EventArgs.Empty);
        });

        try
        {
            var results = await _diagnosticService.RunFullDiagnosticAsync(progress, cancellationToken);
            Summary = new ReadinessSummary(results);
            CheckedAt = DateTime.Now;
            return Summary;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("Состояние рабочего места", "Не удалось выполнить проверку", ex.Message);
            throw;
        }
        finally
        {
            _running = null;
            CurrentStepText = string.Empty;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
