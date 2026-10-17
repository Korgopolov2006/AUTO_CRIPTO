using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="INavigationService"/>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;

    public object? CurrentViewModel { get; private set; }

    public event EventHandler? CurrentViewModelChanged;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void NavigateTo<TViewModel>() where TViewModel : class => NavigateTo(typeof(TViewModel));

    public void NavigateTo(Type viewModelType)
    {
        var previous = CurrentViewModel;
        CurrentViewModel = _serviceProvider.GetService(viewModelType);
        if (!ReferenceEquals(previous, CurrentViewModel))
        {
            (previous as INavigationAware)?.OnNavigatedFrom();
        }

        (CurrentViewModel as INavigationAware)?.OnNavigatedTo();
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
    }
}
