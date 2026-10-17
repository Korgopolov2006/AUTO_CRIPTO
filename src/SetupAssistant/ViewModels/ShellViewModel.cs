using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>ViewModel главного окна: текущая страница, уведомления, индикаторы фоновых операций и прогресс на панели задач.</summary>
public partial class ShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly INotificationService _notifications;
    private readonly IDialogService _dialogService;
    private readonly SetupWizardViewModel _wizard;
    private readonly UninstallViewModel _uninstall;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private bool _isSetupRunning;

    [ObservableProperty]
    private bool _isUninstallRunning;

    [ObservableProperty]
    private TaskbarItemProgressState _taskbarState = TaskbarItemProgressState.None;

    /// <summary>Доля выполненного (0..1) для индикатора на значке в панели задач.</summary>
    [ObservableProperty]
    private double _taskbarProgress;

    /// <summary>Подсказка над значком в области уведомлений: что сейчас делает приложение.</summary>
    [ObservableProperty]
    private string _trayTooltip = "Помощник настройки рабочего места";

    public ObservableCollection<ToastItem> Toasts => _notifications.Items;

    /// <summary>Идёт установка или удаление: закрывать окно опасно.</summary>
    public bool IsOperationRunning => _wizard.IsRunning || _uninstall.IsRunning;

    public ShellViewModel(
        INavigationService navigationService,
        INotificationService notifications,
        IDialogService dialogService,
        SetupWizardViewModel wizard,
        UninstallViewModel uninstall,
        IUserSettingsService userSettings)
    {
        _navigationService = navigationService;
        _notifications = notifications;
        _dialogService = dialogService;
        _wizard = wizard;
        _uninstall = uninstall;

        _navigationService.CurrentViewModelChanged += (_, _) => CurrentPage = _navigationService.CurrentViewModel;
        _wizard.PropertyChanged += OnOperationChanged;
        _uninstall.PropertyChanged += OnOperationChanged;
        _dialogService.ActionPromptVisibilityChanged += (_, _) => UpdateIndicators();

        // При самом первом запуске — короткое приветствие, потом всегда главная.
        if (userSettings.FirstRunCompleted)
        {
            _navigationService.NavigateTo<DashboardViewModel>();
        }
        else
        {
            _navigationService.NavigateTo<FirstRunViewModel>();
        }
    }

    private void OnOperationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StepsViewModelBase.IsRunning) or nameof(StepsViewModelBase.ProgressPercent))
        {
            UpdateIndicators();
        }
    }

    private void UpdateIndicators()
    {
        IsSetupRunning = _wizard.IsRunning;
        IsUninstallRunning = _uninstall.IsRunning;
        OnPropertyChanged(nameof(IsOperationRunning));

        var running = _wizard.IsRunning ? _wizard : _uninstall.IsRunning ? (StepsViewModelBase)_uninstall : null;
        TaskbarProgress = running is null ? 0 : running.ProgressPercent / 100.0;
        TrayTooltip = running is null
            ? "Помощник настройки рабочего места"
            : $"{(running == _wizard ? "Настройка" : "Удаление")}: {running.ProgressPercent}%";

        // Жёлтый индикатор — приложение ждёт действия пользователя (носитель, PIN-код).
        TaskbarState = running is null
            ? TaskbarItemProgressState.None
            : _dialogService.IsActionPromptVisible
                ? TaskbarItemProgressState.Paused
                : TaskbarItemProgressState.Normal;
    }

    [RelayCommand]
    private void DismissToast(ToastItem? item)
    {
        if (item is not null)
        {
            _notifications.Dismiss(item);
        }
    }

    [RelayCommand]
    private static void Exit() => System.Windows.Application.Current.MainWindow?.Close();
}
