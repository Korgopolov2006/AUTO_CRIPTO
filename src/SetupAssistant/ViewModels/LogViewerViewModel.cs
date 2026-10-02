using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Просмотр журнала действий приложения.</summary>
public partial class LogViewerViewModel : ViewModelBase
{
    private readonly ILoggingService _loggingService;
    private readonly INavigationService _navigationService;

    public ObservableCollection<LogEntryModel> Entries { get; }

    public string LogFilePath => _loggingService.CurrentLogFilePath;

    public LogViewerViewModel(ILoggingService loggingService, INavigationService navigationService)
    {
        _loggingService = loggingService;
        _navigationService = navigationService;
        Entries = new ObservableCollection<LogEntryModel>(_loggingService.Entries);
        _loggingService.EntryAdded += (_, entry) => Entries.Add(entry);
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        var directory = Path.GetDirectoryName(LogFilePath);
        if (directory is not null && Directory.Exists(directory))
        {
            System.Diagnostics.Process.Start("explorer.exe", directory);
        }
    }

    [RelayCommand]
    private void GoBack() => _navigationService.NavigateTo<DashboardViewModel>();
}
