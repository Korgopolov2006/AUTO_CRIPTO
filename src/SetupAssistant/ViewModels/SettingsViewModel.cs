using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Просмотр текущей конфигурации приложения (Config.json).</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IDialogService _dialogService;
    private readonly INavigationService _navigationService;
    private readonly IExtensionUpdateService _extensionUpdateService;

    [ObservableProperty]
    private AppConfig _config;

    [ObservableProperty]
    private IReadOnlyList<ExtensionUpdateInfo> _extensionUpdates = Array.Empty<ExtensionUpdateInfo>();

    public SettingsViewModel(
        IConfigService configService,
        IDialogService dialogService,
        INavigationService navigationService,
        IExtensionUpdateService extensionUpdateService)
    {
        _configService = configService;
        _dialogService = dialogService;
        _navigationService = navigationService;
        _extensionUpdateService = extensionUpdateService;
        _config = configService.Current;
    }

    [RelayCommand]
    private async Task CheckExtensionUpdatesAsync()
    {
        IsBusy = true;
        BusyMessage = "Проверка версий расширений...";
        try
        {
            ExtensionUpdates = await _extensionUpdateService.CheckAsync();
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    [RelayCommand]
    private void ReloadConfig()
    {
        Config = _configService.Load();
        _dialogService.ShowInfo("Конфигурация перезагружена из Config.json.");
    }

    [RelayCommand]
    private void GoBack() => _navigationService.NavigateTo<DashboardViewModel>();
}
