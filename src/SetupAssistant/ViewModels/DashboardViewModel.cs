using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Главный экран приложения: сведения о рабочей станции и переход к основным разделам.</summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly IExtensionUpdateService _extensionUpdateService;

    [ObservableProperty]
    private SystemInfo _systemInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateNotice))]
    private string _updateNotice = string.Empty;

    public bool HasUpdateNotice => !string.IsNullOrEmpty(UpdateNotice);

    public string AppName { get; }

    public string AppVersion { get; }

    public DashboardViewModel(
        ISystemService systemService,
        IConfigService configService,
        INavigationService navigationService,
        IExtensionUpdateService extensionUpdateService)
    {
        _navigationService = navigationService;
        _extensionUpdateService = extensionUpdateService;
        _systemInfo = systemService.GetSystemInfo();
        AppName = configService.Current.AppName;
        AppVersion = configService.Current.AppVersion;

        if (configService.Current.Extensions.CheckUpdatesOnStartup)
        {
            _ = CheckExtensionUpdatesAsync();
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
    private void CheckWorkplace() => _navigationService.NavigateTo<DiagnosticsViewModel>();

    [RelayCommand]
    private void SetupWorkplace() => _navigationService.NavigateTo<SetupWizardViewModel>();

    [RelayCommand]
    private void ViewLog() => _navigationService.NavigateTo<LogViewerViewModel>();

    [RelayCommand]
    private void OpenSettings() => _navigationService.NavigateTo<SettingsViewModel>();

    [RelayCommand]
    private void ViewCertificates() => _navigationService.NavigateTo<CertificatesViewModel>();

    [RelayCommand]
    private static void Exit() => System.Windows.Application.Current.Shutdown();
}
