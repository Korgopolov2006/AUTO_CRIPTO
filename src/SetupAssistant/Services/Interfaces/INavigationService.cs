namespace SetupAssistant.Services.Interfaces;

/// <summary>Навигация между экранами (ViewModel) приложения внутри главного окна.</summary>
public interface INavigationService
{
    object? CurrentViewModel { get; }

    event EventHandler? CurrentViewModelChanged;

    void NavigateTo<TViewModel>() where TViewModel : class;
}
