using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Главный экран: готовность рабочего места, быстрое исправление проблем и сведения о станции.</summary>
public partial class DashboardViewModel : ViewModelBase, INavigationAware
{
    private static readonly TimeSpan StatusMaxAge = TimeSpan.FromSeconds(60);
    private const int MaxIssuesShown = 6;

    private readonly INavigationService _navigationService;
    private readonly IExtensionUpdateService _extensionUpdateService;
    private readonly IWorkplaceStatusService _statusService;
    private readonly SetupWizardViewModel _wizard;

    public ObservableCollection<ComponentInfo> Issues { get; } = new();

    [ObservableProperty]
    private SystemInfo _systemInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateNotice))]
    private string _updateNotice = string.Empty;

    /// <summary>Значок и цвет карточки состояния.</summary>
    [ObservableProperty]
    private StatusLevel _readinessLevel = StatusLevel.NotChecked;

    [ObservableProperty]
    private string _readinessTitle = "Состояние не проверено";

    [ObservableProperty]
    private string _readinessText = string.Empty;

    [ObservableProperty]
    private bool _canFix;

    [ObservableProperty]
    private bool _isSetupRunning;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private string _hiddenIssuesText = string.Empty;

    public bool HasUpdateNotice => !string.IsNullOrEmpty(UpdateNotice);

    public bool HasIssues => Issues.Count > 0;

    public string AppName { get; }

    public string AppVersion { get; }

    public DashboardViewModel(
        ISystemService systemService,
        IConfigService configService,
        INavigationService navigationService,
        IExtensionUpdateService extensionUpdateService,
        IWorkplaceStatusService statusService,
        SetupWizardViewModel wizard)
    {
        _navigationService = navigationService;
        _extensionUpdateService = extensionUpdateService;
        _statusService = statusService;
        _wizard = wizard;
        _systemInfo = systemService.GetSystemInfo();
        AppName = configService.Current.AppName;
        AppVersion = configService.Current.AppVersion;

        _statusService.Changed += (_, _) => UpdateReadiness();
        _wizard.PropertyChanged += OnWizardPropertyChanged;
        UpdateReadiness();

        if (configService.Current.Extensions.CheckUpdatesOnStartup)
        {
            _ = CheckExtensionUpdatesAsync();
        }
    }

    private void OnWizardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SetupWizardViewModel.IsRunning) or nameof(SetupWizardViewModel.CurrentStepText))
        {
            UpdateReadiness();
        }
    }

    public void OnNavigatedTo()
    {
        var stale = _statusService.CheckedAt is not { } checkedAt || DateTime.Now - checkedAt > StatusMaxAge;
        if (stale && !_wizard.IsRunning && !_statusService.IsChecking)
        {
            _ = RecheckAsync();
        }
        else
        {
            UpdateReadiness();
        }
    }

    public void OnNavigatedFrom()
    {
    }

    private void UpdateReadiness()
    {
        var summary = _statusService.Summary;
        IsSetupRunning = _wizard.IsRunning;
        IsChecking = _statusService.IsChecking;

        Issues.Clear();
        foreach (var problem in summary.Problems.OrderByDescending(p => p.Status).Take(MaxIssuesShown))
        {
            Issues.Add(problem);
        }

        OnPropertyChanged(nameof(HasIssues));
        HiddenIssuesText = summary.Problems.Count > MaxIssuesShown
            ? $"и ещё {summary.Problems.Count - MaxIssuesShown} — смотрите «Диагностику»"
            : string.Empty;
        CanFix = summary.FixSteps.Count > 0 && !IsSetupRunning && !IsChecking;

        if (IsSetupRunning)
        {
            ReadinessLevel = StatusLevel.InProgress;
            ReadinessTitle = "Выполняется настройка рабочего места";
            ReadinessText = _wizard.CurrentStepText;
        }
        else if (IsChecking)
        {
            ReadinessLevel = StatusLevel.InProgress;
            ReadinessTitle = "Проверяем рабочее место…";
            ReadinessText = _statusService.CurrentStepText;
        }
        else if (_statusService.CheckedAt is null)
        {
            ReadinessLevel = StatusLevel.NotChecked;
            ReadinessTitle = "Состояние не проверено";
            ReadinessText = "Нажмите «Проверить», чтобы узнать, всё ли готово для работы с электронной подписью.";
        }
        else if (summary.FixSteps.Count > 0)
        {
            ReadinessLevel = summary.ErrorCount > 0 ? StatusLevel.Error : StatusLevel.Warning;
            ReadinessTitle = $"Найдено {Plural(summary.Problems.Count, "проблема", "проблемы", "проблем")}";
            ReadinessText = $"Мастер может исправить {summary.Fixable.Count} из них автоматически — нажмите «Исправить».";
        }
        else if (summary.ErrorCount > 0)
        {
            ReadinessLevel = StatusLevel.Error;
            ReadinessTitle = "Есть ошибки, требующие внимания";
            ReadinessText = "Автоматически они не исправляются — подробности в списке ниже и в разделе «Диагностика».";
        }
        else if (summary.WarningCount > 0)
        {
            ReadinessLevel = StatusLevel.Warning;
            ReadinessTitle = $"Рабочее место готово, {Plural(summary.WarningCount, "замечание", "замечания", "замечаний")}";
            ReadinessText = "Замечания не мешают работе, но стоит обратить на них внимание.";
        }
        else
        {
            ReadinessLevel = StatusLevel.Ok;
            ReadinessTitle = "Рабочее место готово";
            ReadinessText = $"Все компоненты установлены и работают. Проверено в {_statusService.CheckedAt:HH:mm}.";
        }
    }

    private static string Plural(int count, string one, string few, string many)
    {
        var n = Math.Abs(count) % 100;
        var last = n % 10;
        var word = n is > 10 and < 20 ? many : last == 1 ? one : last is >= 2 and <= 4 ? few : many;
        return $"{count} {word}";
    }

    private async Task CheckExtensionUpdatesAsync()
    {
        try
        {
            var outdated = (await _extensionUpdateService.CheckAsync()).Where(r => r.IsUpdateAvailable).ToList();
            if (outdated.Count > 0)
            {
                UpdateNotice = "Доступны новые версии расширений браузера: "
                    + string.Join(", ", outdated.Select(r => $"{r.Name} {r.LatestVersion}"))
                    + ". Запустите ExtensionUpdater.exe на компьютере с интернетом.";
            }
        }
        catch (Exception)
        {
            // Фоновая проверка не должна мешать работе приложения.
        }
    }

    [RelayCommand]
    private async Task RecheckAsync()
    {
        if (_statusService.IsChecking || _wizard.IsRunning)
        {
            return;
        }

        try
        {
            await _statusService.RefreshAsync();
        }
        catch (Exception)
        {
            // Причина записана в журнал; карточка остаётся в прежнем состоянии.
        }
    }

    [RelayCommand]
    private async Task FixAsync()
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
    private void ShowSetupProgress() => _navigationService.NavigateTo<SetupWizardViewModel>();

    [RelayCommand]
    private void CheckWorkplace() => _navigationService.NavigateTo<DiagnosticsViewModel>();

    [RelayCommand]
    private void SetupWorkplace() => _navigationService.NavigateTo<SetupWizardViewModel>();

    [RelayCommand]
    private static void Exit() => System.Windows.Application.Current.Shutdown();
}
