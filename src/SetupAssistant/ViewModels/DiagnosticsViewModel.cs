using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Раздел «Диагностика»: полная проверка рабочего места с группами, фильтром и исправлением проблем.</summary>
public partial class DiagnosticsViewModel : ViewModelBase, INavigationAware, IRefreshable
{
    private readonly IWorkplaceStatusService _statusService;
    private readonly IReportService _reportService;
    private readonly ICertificateService _certificateService;
    private readonly ISystemService _systemService;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notifications;
    private readonly INavigationService _navigationService;
    private readonly SetupWizardViewModel _wizard;
    private readonly ICollectionView _view;

    public ObservableCollection<ComponentInfo> Components { get; } = new();

    [ObservableProperty]
    private string _currentStepText = string.Empty;

    [ObservableProperty]
    private bool _hasRun;

    /// <summary>Показывать только предупреждения и ошибки.</summary>
    [ObservableProperty]
    private bool _showOnlyProblems;

    [ObservableProperty]
    private int _okCount;

    [ObservableProperty]
    private int _infoCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _fixableCount;

    /// <summary>Нет строк для показа: диагностику ещё не запускали либо фильтр всё скрыл.</summary>
    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _emptyText = "Диагностика ещё не запускалась";

    [ObservableProperty]
    private string _emptyHint = "Нажмите «Запустить диагностику»: проверка занимает несколько секунд и ничего не устанавливает.";

    /// <summary>Идёт проверка и результатов ещё нет: вместо пустого экрана показываем «скелетон».</summary>
    public bool ShowSkeleton => IsBusy && Components.Count == 0;

    /// <summary>Пустое состояние показываем только когда проверка не идёт.</summary>
    public bool ShowEmpty => IsEmpty && !IsBusy;

    public DiagnosticsViewModel(
        IWorkplaceStatusService statusService,
        IReportService reportService,
        ICertificateService certificateService,
        ISystemService systemService,
        IDialogService dialogService,
        INotificationService notifications,
        INavigationService navigationService,
        SetupWizardViewModel wizard)
    {
        _statusService = statusService;
        _reportService = reportService;
        _certificateService = certificateService;
        _systemService = systemService;
        _dialogService = dialogService;
        _notifications = notifications;
        _navigationService = navigationService;
        _wizard = wizard;

        _view = CollectionViewSource.GetDefaultView(Components);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ComponentInfo.Category)));
        _view.SortDescriptions.Add(new SortDescription(nameof(ComponentInfo.CategoryOrder), ListSortDirection.Ascending));
        _view.Filter = item => !ShowOnlyProblems || item is ComponentInfo { IsProblem: true };

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IsBusy) or nameof(IsEmpty))
            {
                OnPropertyChanged(nameof(ShowSkeleton));
                OnPropertyChanged(nameof(ShowEmpty));
            }
        };

        _statusService.Changed += (_, _) => OnStatusChanged();
        LoadResults();
    }

    partial void OnShowOnlyProblemsChanged(bool value)
    {
        _view.Refresh();
        UpdateEmptyState();
    }

    public void OnNavigatedTo()
    {
        if (!_statusService.IsChecking)
        {
            LoadResults();
        }
    }

    /// <summary>F5: запустить диагностику заново.</summary>
    public void Refresh() => _ = RunDiagnosticAsync();

    public void OnNavigatedFrom()
    {
    }

    private void OnStatusChanged()
    {
        IsBusy = _statusService.IsChecking;
        CurrentStepText = _statusService.CurrentStepText;

        if (!_statusService.IsChecking)
        {
            LoadResults();
        }
    }

    private void LoadResults()
    {
        var summary = _statusService.Summary;
        Components.Clear();
        foreach (var component in summary.Results)
        {
            Components.Add(component);
        }

        HasRun = _statusService.CheckedAt is not null;
        OkCount = summary.OkCount;
        InfoCount = summary.InfoCount;
        WarningCount = summary.WarningCount;
        ErrorCount = summary.ErrorCount;
        FixableCount = summary.Fixable.Count;
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        IsEmpty = !_view.Cast<object>().Any();
        EmptyText = !HasRun
            ? "Диагностика ещё не запускалась"
            : "Проблем не найдено";
        EmptyHint = !HasRun
            ? "Нажмите «Запустить диагностику»: проверка занимает несколько секунд и ничего не устанавливает."
            : "Все проверки пройдены. Выключите фильтр «Только проблемы», чтобы увидеть весь список.";
    }

    [RelayCommand]
    private async Task RunDiagnosticAsync()
    {
        if (_statusService.IsChecking)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _statusService.RefreshAsync();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Не удалось выполнить диагностику: {ex.Message}");
        }
        finally
        {
            IsBusy = _statusService.IsChecking;
        }
    }

    [RelayCommand]
    private async Task FixComponentAsync(ComponentInfo? component)
    {
        if (component?.FixStep is not { } kind)
        {
            return;
        }

        _navigationService.NavigateTo<SetupWizardViewModel>();
        await _wizard.StartFixAsync(new[] { kind });
    }

    [RelayCommand]
    private async Task FixAllAsync()
    {
        var kinds = _statusService.Summary.FixSteps;
        if (kinds.Count == 0)
        {
            return;
        }

        _navigationService.NavigateTo<SetupWizardViewModel>();
        await _wizard.StartFixAsync(kinds);
    }

    [RelayCommand]
    private void CopyDetails(ComponentInfo? component)
    {
        if (component is null)
        {
            return;
        }

        CopyToClipboard(component.ToClipboardText());
        _notifications.Show(ToastKind.Info, "Скопировано", component.Name);
    }

    [RelayCommand]
    private void CopyAll()
    {
        var items = _view.Cast<ComponentInfo>().ToList();
        if (items.Count == 0)
        {
            return;
        }

        CopyToClipboard(string.Join("\n\n", items.Select(c => c.ToClipboardText())));
        _notifications.Show(ToastKind.Info, "Скопировано", $"Строк: {items.Count}");
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Буфер обмена занят другим приложением — пользователь может повторить.
        }
    }

    [RelayCommand]
    private void ExportReport()
    {
        if (Components.Count == 0)
        {
            _dialogService.ShowWarning("Сначала выполните диагностику рабочего места.");
            return;
        }

        var path = _dialogService.ShowSaveFileDialog(
            $"Отчёт_диагностики_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
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
                Components = Components.ToList(),
                Certificates = _certificateService.GetCertificates().ToList(),
                Errors = Components.Where(c => c.Status == StatusLevel.Error && c.ErrorMessage is not null)
                    .Select(c => $"{c.Name}: {c.ErrorMessage}").ToList()
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
}
