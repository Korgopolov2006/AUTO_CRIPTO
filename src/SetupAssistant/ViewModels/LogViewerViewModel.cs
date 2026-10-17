using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Уровни фильтра журнала.</summary>
public enum LogLevelFilter
{
    All,
    Information,
    Warning,
    Error
}

/// <summary>Просмотр журнала действий приложения: фильтр по уровню, поиск, слежение за новыми записями.</summary>
public partial class LogViewerViewModel : ViewModelBase, INavigationAware
{
    private readonly ILoggingService _loggingService;
    private readonly INotificationService _notifications;
    private readonly ICollectionView _view;

    public ObservableCollection<LogEntryModel> Entries { get; } = new();

    public IReadOnlyList<LogFilterOption> LevelOptions { get; } = new[]
    {
        new LogFilterOption(LogLevelFilter.All, "Все записи"),
        new LogFilterOption(LogLevelFilter.Information, "Информация"),
        new LogFilterOption(LogLevelFilter.Warning, "Предупреждения"),
        new LogFilterOption(LogLevelFilter.Error, "Ошибки")
    };

    [ObservableProperty]
    private LogLevelFilter _levelFilter = LogLevelFilter.All;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Прокручивать список к новым записям.</summary>
    [ObservableProperty]
    private bool _followNewEntries = true;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _countText = string.Empty;

    /// <summary>Просит страницу прокрутить список к последней записи.</summary>
    public event EventHandler? ScrollToEndRequested;

    public string LogFilePath => _loggingService.CurrentLogFilePath;

    public LogViewerViewModel(ILoggingService loggingService, INotificationService notifications)
    {
        _loggingService = loggingService;
        _notifications = notifications;

        _view = CollectionViewSource.GetDefaultView(Entries);
        _view.Filter = Matches;
    }

    partial void OnLevelFilterChanged(LogLevelFilter value) => RefreshView();

    partial void OnSearchTextChanged(string value) => RefreshView();

    public void OnNavigatedTo()
    {
        Entries.Clear();
        foreach (var entry in _loggingService.Entries.ToList())
        {
            Entries.Add(entry);
        }

        _loggingService.EntryAdded += OnEntryAdded;
        RefreshView();
        if (FollowNewEntries)
        {
            ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void OnNavigatedFrom() => _loggingService.EntryAdded -= OnEntryAdded;

    /// <summary>Записи пишутся из любых потоков (диагностика идёт в фоне), а коллекция принадлежит UI.</summary>
    private void OnEntryAdded(object? sender, LogEntryModel entry)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Entries.Add(entry);
            UpdateCounters();
            if (FollowNewEntries && Matches(entry))
            {
                ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private bool Matches(object item)
    {
        if (item is not LogEntryModel entry)
        {
            return false;
        }

        var levelOk = LevelFilter switch
        {
            LogLevelFilter.Information => entry.Level <= LogLevel.Information,
            LogLevelFilter.Warning => entry.Level == LogLevel.Warning,
            LogLevelFilter.Error => entry.Level >= LogLevel.Error,
            _ => true
        };

        if (!levelOk)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var query = SearchText.Trim();
        return entry.Action.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || entry.Result.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || (entry.ErrorText?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    private void RefreshView()
    {
        _view.Refresh();
        UpdateCounters();
    }

    private void UpdateCounters()
    {
        var shown = _view.Cast<object>().Count();
        IsEmpty = shown == 0;
        CountText = shown == Entries.Count ? $"Записей: {shown}" : $"Показано {shown} из {Entries.Count}";
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

    /// <summary>Копирует выделенные строки; если ничего не выделено — все показанные.</summary>
    [RelayCommand]
    private void Copy(IList? selected)
    {
        var entries = selected is { Count: > 0 } ? selected.Cast<LogEntryModel>().ToList() : _view.Cast<LogEntryModel>().ToList();
        if (entries.Count == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, entries.OrderBy(e => e.Timestamp).Select(e => e.ToString())));
            _notifications.Show(ToastKind.Info, "Скопировано", $"Записей: {entries.Count}");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Буфер обмена занят — можно повторить.
        }
    }
}

/// <summary>Пункт выпадающего списка фильтра журнала.</summary>
public sealed record LogFilterOption(LogLevelFilter Value, string Title);
