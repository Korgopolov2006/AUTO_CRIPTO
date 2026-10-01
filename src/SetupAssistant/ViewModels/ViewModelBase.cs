using CommunityToolkit.Mvvm.ComponentModel;

namespace SetupAssistant.ViewModels;

/// <summary>Базовый класс для всех ViewModel приложения.</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = string.Empty;
}
