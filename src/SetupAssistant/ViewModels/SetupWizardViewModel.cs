using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Helpers;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Мастер настройки рабочего места: последовательная установка и проверка выбранных компонентов.</summary>
public partial class SetupWizardViewModel : StepsViewModelBase, INavigationAware
{
    private readonly ISetupOrchestrationService _orchestrationService;
    private readonly IDialogService _dialogService;
    private readonly IPreflightService _preflightService;
    private readonly IWorkplaceStatusService _statusService;
    private readonly INotificationService _notifications;
    private readonly IReportService _reportService;
    private readonly ISystemService _systemService;
    private readonly ICertificateService _certificateService;
    private readonly ILoggingService _logger;

    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ICollectionView _stepsView;

    public ObservableCollection<PreflightCheck> PreflightChecks { get; } = new();

    [ObservableProperty]
    private bool _isPreflightRunning;

    [ObservableProperty]
    private bool _hasBlockingPreflight;

    /// <summary>Устанавливать и уже установленные компоненты.</summary>
    [ObservableProperty]
    private bool _forceReinstall;

    /// <summary>Скрыть из списка завершённые успешно и пропущенные шаги — удобно, когда шагов много.</summary>
    [ObservableProperty]
    private bool _hideCompleted;

    [ObservableProperty]
    private bool _hasSummary;

    [ObservableProperty]
    private StatusLevel _summaryLevel;

    [ObservableProperty]
    private string _summaryTitle = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _rebootRequired;

    [ObservableProperty]
    private bool _hasFailedSteps;

    protected override string IdleText => "Выберите компоненты и нажмите «Начать настройку»";

    public bool CanEditSelection => !IsRunning;

    public SetupWizardViewModel(
        ISetupOrchestrationService orchestrationService,
        IDialogService dialogService,
        IConfigService configService,
        IPreflightService preflightService,
        IWorkplaceStatusService statusService,
        INotificationService notifications,
        IReportService reportService,
        ISystemService systemService,
        ICertificateService certificateService,
        ILoggingService logger)
    {
        _orchestrationService = orchestrationService;
        _dialogService = dialogService;
        _preflightService = preflightService;
        _statusService = statusService;
        _notifications = notifications;
        _reportService = reportService;
        _systemService = systemService;
        _certificateService = certificateService;
        _logger = logger;

        foreach (var step in SetupPlan.Build(configService))
        {
            Steps.Add(step);
            if (step.IsSelectable)
            {
                step.PropertyChanged += OnStepPropertyChanged;
            }
        }

        _stepsView = CollectionViewSource.GetDefaultView(Steps);
        _stepsView.Filter = item => !HideCompleted || item is not SetupStepModel { Status: StatusLevel.Ok or StatusLevel.Skipped };

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsRunning))
            {
                OnPropertyChanged(nameof(CanEditSelection));
            }
        };

        UpdateProgress();
    }

    partial void OnHideCompletedChanged(bool value) => _stepsView.Refresh();

    /// <summary>Проверка парного шага следует за выбором установки.</summary>
    private void OnStepPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SetupStepModel install || e.PropertyName != nameof(SetupStepModel.IsSelected))
        {
            return;
        }

        foreach (var verify in Steps.Where(s => s.PairedInstallKind == install.Kind))
        {
            verify.IsSelected = install.IsSelected;
        }
    }

    protected override void OnProgressUpdated() => _stepsView.Refresh();

    public void OnNavigatedTo()
    {
        if (!IsRunning && !IsCompleted)
        {
            _ = RunPreflightAsync();
        }
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private async Task RunPreflightAsync()
    {
        if (IsPreflightRunning)
        {
            return;
        }

        IsPreflightRunning = true;
        try
        {
            var checks = await _preflightService.RunAsync();
            PreflightChecks.Clear();
            foreach (var check in checks)
            {
                PreflightChecks.Add(check);
            }

            HasBlockingPreflight = checks.Any(c => c.IsBlocking);
        }
        catch (Exception ex)
        {
            _logger.Warning("Предварительная проверка", "Не удалось выполнить", ex.Message);
        }
        finally
        {
            IsPreflightRunning = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var step in Steps.Where(s => s.IsSelectable))
        {
            step.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var step in Steps.Where(s => s.IsSelectable))
        {
            step.IsSelected = false;
        }
    }

    /// <summary>Выбирает только то, что по последней диагностике требует исправления.</summary>
    [RelayCommand]
    private async Task SelectProblemsAsync()
    {
        var summary = _statusService.CheckedAt is null ? await _statusService.RefreshAsync() : _statusService.Summary;
        if (summary.FixSteps.Count == 0)
        {
            _notifications.Show(ToastKind.Info, "Всё в порядке", "Диагностика не нашла проблем, которые исправляет мастер.");
            return;
        }

        SetupPlan.SelectOnly(Steps, summary.FixSteps);
    }

    [RelayCommand]
    private async Task StartSetupAsync()
    {
        if (IsRunning)
        {
            return;
        }

        if (HasBlockingPreflight)
        {
            _dialogService.ShowError("Предварительная проверка выявила проблему, из-за которой установка невозможна. Устраните её (см. список вверху) и повторите.", "Установка невозможна");
            return;
        }

        var selected = Steps.Count(s => s.IsSelectable && s.IsSelected);
        if (selected == 0)
        {
            _dialogService.ShowWarning("Выберите хотя бы один компонент для установки.");
            return;
        }

        if (!_dialogService.Confirm($"Будет выполнена установка и настройка выбранных компонентов ({selected}). Продолжить?"))
        {
            return;
        }

        await RunAsync(Steps.ToList(), resetAll: true);
    }

    /// <summary>Запуск исправления: выбираются только указанные шаги. Вызывается с главной и из диагностики.</summary>
    public async Task StartFixAsync(IReadOnlyCollection<SetupStepKind> kinds, bool askConfirmation = true)
    {
        if (IsRunning)
        {
            return;
        }

        SetupPlan.SelectOnly(Steps, kinds);
        var titles = Steps.Where(s => s.IsSelectable && s.IsSelected).Select(s => s.Title).ToList();
        if (titles.Count == 0)
        {
            return;
        }

        if (HasBlockingPreflight)
        {
            _dialogService.ShowError("Предварительная проверка выявила проблему, из-за которой установка невозможна. Устраните её и повторите.", "Установка невозможна");
            return;
        }

        if (askConfirmation && !_dialogService.Confirm("Будут выполнены шаги:\n• " + string.Join("\n• ", titles) + "\n\nПродолжить?", "Исправление проблем"))
        {
            return;
        }

        await RunAsync(Steps.ToList(), resetAll: true);
    }

    /// <summary>Продолжение настройки после перезагрузки: определяет, что осталось неисправленным, и исправляет.</summary>
    public async Task ResumeAfterRebootAsync()
    {
        ResumeRegistration.Unregister();
        _logger.Info("Продолжение после перезагрузки", "Приложение запущено после перезагрузки: проверяем, что осталось сделать");

        await RunPreflightAsync();
        var summary = await _statusService.RefreshAsync();
        if (summary.FixSteps.Count == 0)
        {
            _notifications.Show(ToastKind.Success, "Рабочее место готово", "После перезагрузки проблем, которые исправляет мастер, не осталось.");
            return;
        }

        await StartFixAsync(summary.FixSteps, askConfirmation: false);
    }

    [RelayCommand]
    private async Task RetryStepAsync(SetupStepModel? step)
    {
        if (step is null || IsRunning)
        {
            return;
        }

        var run = new List<SetupStepModel> { step };
        run.AddRange(Steps.Where(s => s.PairedInstallKind == step.Kind));
        run.Add(Steps.First(s => s.Kind == SetupStepKind.FinalDiagnostic));

        foreach (var s in run)
        {
            s.IsSelected = true;
        }

        await RunAsync(run, resetAll: false);
    }

    [RelayCommand]
    private async Task RetryFailedAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var failed = Steps.Where(s => s.CanRetry).ToList();
        if (failed.Count == 0)
        {
            return;
        }

        var run = failed.ToList();
        foreach (var install in failed)
        {
            run.AddRange(Steps.Where(s => s.PairedInstallKind == install.Kind));
        }

        run.Add(Steps.First(s => s.Kind == SetupStepKind.FinalDiagnostic));
        foreach (var s in run)
        {
            s.IsSelected = true;
        }

        await RunAsync(run.Distinct().OrderBy(s => Steps.IndexOf(s)).ToList(), resetAll: false);
    }

    private async Task RunAsync(IReadOnlyList<SetupStepModel> stepsToRun, bool resetAll)
    {
        foreach (var step in resetAll ? Steps.ToList() : stepsToRun)
        {
            ResetResult(step);
        }

        HasSummary = false;
        IsRunning = true;
        IsCompleted = false;
        UpdateProgress();
        _cancellationTokenSource = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();

        var progress = new Progress<SetupStepModel>(_ => UpdateProgress());

        try
        {
            await _orchestrationService.RunSetupAsync(stepsToRun, progress, new SetupRunOptions(ForceReinstall), _cancellationTokenSource.Token);
            IsCompleted = true;
            stopwatch.Stop();
            BuildSummary(stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            _notifications.Show(ToastKind.Warning, "Настройка отменена", "Выполненные шаги остались как есть, остальные не запускались.");
            BuildSummary(stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Ошибка настройки рабочего места: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            UpdateProgress();
        }
    }

    private static void ResetResult(SetupStepModel step)
    {
        step.Status = StatusLevel.NotChecked;
        step.Details = null;
        step.NeedsReboot = false;
        step.IsExpanded = false;
    }

    private void BuildSummary(TimeSpan duration)
    {
        var ok = Steps.Count(s => s.Status == StatusLevel.Ok);
        var warnings = Steps.Count(s => s.Status == StatusLevel.Warning);
        var errors = Steps.Count(s => s.Status == StatusLevel.Error);
        var skipped = Steps.Count(s => s.Status == StatusLevel.Skipped);

        RebootRequired = Steps.Any(s => s.NeedsReboot);
        HasFailedSteps = Steps.Any(s => s.CanRetry);

        SummaryLevel = errors > 0 ? StatusLevel.Error : warnings > 0 ? StatusLevel.Warning : StatusLevel.Ok;
        SummaryTitle = errors > 0
            ? "Настройка завершена с ошибками"
            : warnings > 0
                ? "Настройка завершена с замечаниями"
                : "Настройка успешно завершена";

        var text = $"Успешно: {ok} · Предупреждений: {warnings} · Ошибок: {errors}";
        if (skipped > 0)
        {
            text += $" · Пропущено: {skipped}";
        }

        text += $" · Время: {(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
        if (RebootRequired)
        {
            text += "\nДля завершения установки требуется перезагрузка компьютера.";
        }

        if (HasFailedSteps)
        {
            text += "\nУ шагов с ошибкой или предупреждением есть кнопка «Повторить».";
        }

        SummaryText = text;
        HasSummary = true;

        _notifications.Show(
            SummaryLevel == StatusLevel.Ok ? ToastKind.Success : SummaryLevel == StatusLevel.Warning ? ToastKind.Warning : ToastKind.Error,
            SummaryTitle,
            "Подробности — в сводке над списком шагов.");
    }

    [RelayCommand]
    private void ToggleExpanded(SetupStepModel? step)
    {
        if (step is not null)
        {
            step.IsExpanded = !step.IsExpanded;
        }
    }

    [RelayCommand]
    private void CancelSetup() => _cancellationTokenSource?.Cancel();

    [RelayCommand]
    private void ExportReport()
    {
        var path = _dialogService.ShowSaveFileDialog(
            $"Отчёт_настройки_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
            "PDF файлы (*.pdf)|*.pdf|Текстовые файлы (*.txt)|*.txt");
        if (path is null)
        {
            return;
        }

        try
        {
            var report = new ReportModel
            {
                SystemInfo = _systemService.GetSystemInfo(),
                Components = _statusService.Summary.Results.ToList(),
                Certificates = _certificateService.GetCertificates().ToList(),
                Errors = Steps.Where(s => s.Status is StatusLevel.Error or StatusLevel.Warning)
                    .Select(s => $"{s.Title}: {s.Details}").ToList()
            };

            var format = path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? ReportFormat.Pdf : ReportFormat.Txt;
            _reportService.Export(report, format, path);
            _notifications.Show(ToastKind.Success, "Отчёт сохранён", path);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Не удалось сохранить отчёт: {ex.Message}");
        }
    }

    /// <summary>Перезагрузка с автоматическим продолжением настройки после входа в систему.</summary>
    [RelayCommand]
    private void RestartAndResume()
    {
        if (!_dialogService.Confirm(
                "Компьютер будет перезагружен через 30 секунд. После входа в систему приложение запустится само и продолжит настройку " +
                "(потребуется подтвердить запуск от имени администратора).\n\nСохраните открытые документы. Перезагрузить сейчас?",
                "Перезагрузка"))
        {
            return;
        }

        try
        {
            ResumeRegistration.Register();
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 30 /c \"Перезагрузка для завершения настройки рабочего места\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            _logger.Info("Перезагрузка", "Запланирована перезагрузка через 30 секунд, продолжение настройки зарегистрировано");
            _notifications.Show(ToastKind.Info, "Перезагрузка через 30 секунд", "Отменить: команда shutdown /a в командной строке.");
        }
        catch (Exception ex)
        {
            ResumeRegistration.Unregister();
            _dialogService.ShowError($"Не удалось запланировать перезагрузку: {ex.Message}");
        }
    }
}
