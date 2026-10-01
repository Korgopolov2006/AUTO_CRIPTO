using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Раздел "Диагностика" — независимая полная проверка рабочего места без установки компонентов.</summary>
public partial class DiagnosticsViewModel : ViewModelBase
{
    private readonly IDiagnosticService _diagnosticService;
    private readonly IReportService _reportService;
    private readonly ICertificateService _certificateService;
    private readonly ISystemService _systemService;
    private readonly IDialogService _dialogService;
    private readonly INavigationService _navigationService;

    public ObservableCollection<ComponentInfo> Components { get; } = new();

    [ObservableProperty]
    private string _currentStepText = string.Empty;

    [ObservableProperty]
    private bool _hasRun;

    public DiagnosticsViewModel(
        IDiagnosticService diagnosticService,
        IReportService reportService,
        ICertificateService certificateService,
        ISystemService systemService,
        IDialogService dialogService,
        INavigationService navigationService)
    {
        _diagnosticService = diagnosticService;
        _reportService = reportService;
        _certificateService = certificateService;
        _systemService = systemService;
        _dialogService = dialogService;
        _navigationService = navigationService;
    }

    [RelayCommand]
    private async Task RunDiagnosticAsync()
    {
        IsBusy = true;
        Components.Clear();
        var progress = new Progress<string>(text => CurrentStepText = text);

        try
        {
            var results = await _diagnosticService.RunFullDiagnosticAsync(progress);
            foreach (var component in results)
            {
                Components.Add(component);
            }
            HasRun = true;
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"Не удалось выполнить диагностику: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            CurrentStepText = string.Empty;
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
        _dialogService.ShowInfo("Отчёт успешно сохранён.");
    }

    [RelayCommand]
    private void GoBack() => _navigationService.NavigateTo<DashboardViewModel>();
}
