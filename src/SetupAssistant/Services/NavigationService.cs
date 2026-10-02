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

    public void NavigateTo<TViewModel>() where TViewModel : class
    {
        CurrentViewModel = _serviceProvider.GetService(typeof(TViewModel));
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
    }
}
