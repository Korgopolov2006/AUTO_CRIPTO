using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Главный экран: сведения о рабочем месте (домен, что установлено), быстрый выбор недостающего и данные станции.</summary>
public partial class DashboardViewModel : ViewModelBase, INavigationAware, IRefreshable
{
    private static readonly TimeSpan StatusMaxAge = TimeSpan.FromSeconds(60);

    private readonly INavigationService _navigationService;
    private readonly IExtensionUpdateService _extensionUpdateService;
    private readonly IWorkplaceStatusService _statusService;
    private readonly SetupWizardViewModel _wizard;

    /// <summary>Обзор: домен, установленные и неустановленные компоненты. Не ошибки, а сведения.</summary>
    public ObservableCollection<ComponentInfo> Overview { get; } = new();

    [ObservableProperty]
    private SystemInfo _systemInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateNotice))]
    private string _updateNotice = string.Empty;

    /// <summary>Значок карточки: сведения (Info), предупреждение (нашлось устаревшее), ход проверки.</summary>
    [ObservableProperty]
    private StatusLevel _readinessLevel = StatusLevel.NotChecked;

    [ObservableProperty]
    private string _readinessTitle = "Состояние не проверено";

    [ObservableProperty]
    private string _readinessText = string.Empty;

    /// <summary>Есть недостающие компоненты, которые можно выбрать в мастере.</summary>
    [ObservableProperty]
    private bool _canSelectMissing;

    [ObservableProperty]
    private bool _isSetupRunning;

    [ObservableProperty]
    private bool _isChecking;

    public bool HasUpdateNotice => !string.IsNullOrEmpty(UpdateNotice);

    public bool HasOverview => Overview.Count > 0;

    /// <summary>Идёт проверка и обзора ещё нет: показываем «скелетон».</summary>
    public bool ShowOverviewSkeleton => IsChecking && !HasOverview;

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

    /// <summary>F5: заново собрать сведения о рабочем месте.</summary>
    public void Refresh() => _ = RecheckAsync();

    public void OnNavigatedFrom()
    {
    }

    private void UpdateReadiness()
    {
        var summary = _statusService.Summary;
        IsSetupRunning = _wizard.IsRunning;
        IsChecking = _statusService.IsChecking;

        // Домен — первой строкой, затем компоненты; остальные проверки живут в «Диагностике».
        Overview.Clear();
        foreach (var item in summary.Results.Where(c => c.ShowOnDashboard).OrderByDescending(c => c.IsDeviceManagement))
        {
            Overview.Add(item);
        }

        OnPropertyChanged(nameof(HasOverview));
        OnPropertyChanged(nameof(ShowOverviewSkeleton));
        CanSelectMissing = summary.Missing.Count > 0 && !IsSetupRunning && !IsChecking;

        if (IsSetupRunning)
        {
            ReadinessLevel = StatusLevel.InProgress;
            ReadinessTitle = "Выполняется настройка рабочего места";
            ReadinessText = _wizard.CurrentStepText;
        }
        else if (IsChecking)
        {
            ReadinessLevel = StatusLevel.InProgress;
            ReadinessTitle = "Собираем сведения о рабочем месте…";
            ReadinessText = _statusService.CurrentStepText;
        }
        else if (_statusService.CheckedAt is null)
        {
            ReadinessLevel = StatusLevel.NotChecked;
            ReadinessTitle = "Сведения ещё не собраны";
            ReadinessText = "Нажмите «Проверить снова», чтобы увидеть, что установлено на этом компьютере.";
        }
        else
        {
            var components = Overview.Where(c => !c.IsDeviceManagement && c.Type != ComponentType.Token).ToList();
            var installed = components.Count(c => c.Status is StatusLevel.Ok or StatusLevel.Warning);
            var domain = Overview.FirstOrDefault(c => c.IsDeviceManagement);
            var domainText = domain is null ? string.Empty : domain.Status == StatusLevel.Ok ? "Компьютер в домене или управляется." : "Компьютер не в домене.";
            var hasOutdated = summary.WarningCount + summary.ErrorCount > 0;

            ReadinessLevel = summary.ErrorCount > 0 ? StatusLevel.Error : summary.WarningCount > 0 ? StatusLevel.Warning : StatusLevel.Info;
            ReadinessTitle = hasOutdated ? "Рабочее место: есть что обновить" : "Рабочее место: сведения";
            ReadinessText = $"Установлено компонентов: {installed} из {components.Count}. {domainText}".Trim()
                            + (hasOutdated ? " Версия некоторых компонентов ниже рекомендуемой." : string.Empty);
        }
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

    /// <summary>Открывает мастер с уже отмеченными недостающими компонентами. Ничего не запускает: не всё нужно на каждом компьютере.</summary>
    [RelayCommand]
    private void SelectMissing()
    {
        var kinds = _statusService.Summary.FixSteps;
        if (kinds.Count == 0)
        {
            return;
        }

        _wizard.PrepareSelection(kinds);
        _navigationService.NavigateTo<SetupWizardViewModel>();
    }

    [RelayCommand]
    private void ShowSetupProgress() => _navigationService.NavigateTo<SetupWizardViewModel>();

    [RelayCommand]
    private void CheckWorkplace() => _navigationService.NavigateTo<DiagnosticsViewModel>();

    [RelayCommand]
    private void SetupWorkplace() => _navigationService.NavigateTo<SetupWizardViewModel>();

    [RelayCommand]
    private static void Exit() => System.Windows.Application.Current.MainWindow?.Close();
}
