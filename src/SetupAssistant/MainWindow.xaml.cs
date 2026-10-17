using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ModernWpf;
using ModernWpf.Controls;
using SetupAssistant.Helpers;
using SetupAssistant.Services.Interfaces;
using SetupAssistant.ViewModels;

namespace SetupAssistant;

/// <summary>Главное окно приложения — боковая навигация и хост текущей страницы (Shell).</summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private bool _syncingSelection;
    private bool _micaApplied;

    public MainWindow(ShellViewModel viewModel, INavigationService navigationService, IDialogService dialogService)
    {
        InitializeComponent();
        _shell = viewModel;
        _navigationService = navigationService;
        _dialogService = dialogService;
        DataContext = viewModel;

        // Экраны открываются и кнопками на страницах, поэтому выделение в меню следует за текущей страницей.
        _navigationService.CurrentViewModelChanged += (_, _) =>
        {
            SelectItemForCurrentPage();
            AnimatePage();
        };
        Loaded += (_, _) => SelectItemForCurrentPage();

        SourceInitialized += (_, _) => ApplyBackdrop();
        ThemeManager.Current.ActualApplicationThemeChanged += (_, _) => UpdateBackdropTheme();
    }

    /// <summary>Mica на Windows 11: окно прозрачное, фон рисует система. На старых системах остаётся фон темы.</summary>
    private void ApplyBackdrop()
    {
        _micaApplied = MicaBackdrop.Apply(this, ThemeManager.Current.ActualApplicationTheme == ApplicationTheme.Dark);
        if (_micaApplied)
        {
            Background = Brushes.Transparent;
        }
    }

    private void UpdateBackdropTheme()
    {
        if (_micaApplied)
        {
            MicaBackdrop.SetDark(new WindowInteropHelper(this).Handle, ThemeManager.Current.ActualApplicationTheme == ApplicationTheme.Dark);
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingSelection || args.SelectedItemContainer?.Tag is not Type viewModelType)
        {
            return;
        }

        if (_navigationService.CurrentViewModel?.GetType() != viewModelType)
        {
            _navigationService.NavigateTo(viewModelType);
        }
    }

    private void SelectItemForCurrentPage()
    {
        var current = _navigationService.CurrentViewModel?.GetType();
        _syncingSelection = true;
        try
        {
            Navigation.SelectedItem = Navigation.MenuItems.OfType<object>().Concat(Navigation.FooterMenuItems.OfType<object>())
                .OfType<NavigationViewItem>()
                .FirstOrDefault(item => item.Tag is Type type && type == current);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>Короткое появление страницы: лёгкий сдвиг снизу и проявление.</summary>
    private void AnimatePage()
    {
        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        PageShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, duration) { EasingFunction = ease });
    }

    /// <summary>Установка и удаление нельзя прерывать закрытием окна без предупреждения.</summary>
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_shell.IsOperationRunning
            && !_dialogService.Confirm(
                "Сейчас выполняется установка или удаление компонентов. Если закрыть приложение, процесс может остановиться на полпути " +
                "и оставить компоненты в неполном состоянии.\n\nВсё равно закрыть?",
                "Операция не завершена"))
        {
            e.Cancel = true;
        }
    }
}
