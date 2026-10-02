using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>ViewModel главного окна — хостит текущую страницу приложения через сервис навигации.</summary>
public partial class ShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private object? _currentPage;

    public ShellViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
        _navigationService.CurrentViewModelChanged += (_, _) => CurrentPage = _navigationService.CurrentViewModel;
        _navigationService.NavigateTo<DashboardViewModel>();
    }

    [RelayCommand]
    private static void Exit() => System.Windows.Application.Current.Shutdown();
}
