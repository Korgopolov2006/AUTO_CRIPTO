using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using H.NotifyIcon.Core;
using ModernWpf;
using ModernWpf.Controls;
using SetupAssistant.Helpers;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;
using SetupAssistant.ViewModels;

namespace SetupAssistant;

/// <summary>Главное окно приложения — боковая навигация и хост текущей страницы (Shell).</summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly IUserSettingsService _userSettings;
    private bool _syncingSelection;
    private bool _micaApplied;

    public MainWindow(
        ShellViewModel viewModel,
        INavigationService navigationService,
        IDialogService dialogService,
        IUserSettingsService userSettings,
        INotificationService notifications)
    {
        InitializeComponent();
        _shell = viewModel;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _userSettings = userSettings;
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

        // Масштаб интерфейса из настроек (для крупного шрифта и плохого зрения).
        ApplyScale();
        _userSettings.UiScaleChanged += (_, _) => ApplyScale();

        // Пока окно свёрнуто или не в фокусе, о завершении операции сообщает уведомление Windows.
        notifications.ToastShown += OnToastShown;
        Closed += (_, _) => Tray.Dispose();
    }

    private void ApplyScale()
    {
        var scale = _userSettings.UiScale;
        Root.LayoutTransform = scale > 1.001 ? new ScaleTransform(scale, scale) : Transform.Identity;
    }

    /// <summary>Mica на Windows 11: окно прозрачное, фон рисует система. На старых системах и при высокой контрастности остаётся фон темы.</summary>
    private void ApplyBackdrop()
    {
        if (SystemParameters.HighContrast)
        {
            return;
        }

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

    private IEnumerable<NavigationViewItem> NavigationItems() =>
        Navigation.MenuItems.OfType<object>().Concat(Navigation.FooterMenuItems.OfType<object>()).OfType<NavigationViewItem>();

    private void SelectItemForCurrentPage()
    {
        var current = _navigationService.CurrentViewModel?.GetType();
        _syncingSelection = true;
        try
        {
            Navigation.SelectedItem = NavigationItems().FirstOrDefault(item => item.Tag is Type type && type == current);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>Горячие клавиши: Ctrl+1…9 — разделы по порядку меню, F5 — обновить текущую страницу.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Пока открыто модальное окно, клавиши принадлежат ему.
        if (Keyboard.FocusedElement is DependencyObject focused && IsInsideDialog(focused))
        {
            return;
        }

        if (e.Key == Key.F5)
        {
            if (_navigationService.CurrentViewModel is IRefreshable refreshable)
            {
                refreshable.Refresh();
                e.Handled = true;
            }

            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        var number = e.Key is >= Key.D1 and <= Key.D9 ? e.Key - Key.D0
            : e.Key is >= Key.NumPad1 and <= Key.NumPad9 ? e.Key - Key.NumPad0
            : 0;
        if (number == 0)
        {
            return;
        }

        var target = NavigationItems().ElementAtOrDefault(number - 1);
        if (target is not null)
        {
            Navigation.SelectedItem = target;
            e.Handled = true;
        }
    }

    private static bool IsInsideDialog(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ContentDialog)
            {
                return true;
            }

            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    /// <summary>Короткое появление страницы: лёгкий сдвиг снизу и проявление.</summary>
    private void AnimatePage()
    {
        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        PageShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, duration) { EasingFunction = ease });
    }

    private void OnToastShown(object? sender, ToastItem toast)
    {
        if (IsActive && WindowState != WindowState.Minimized)
        {
            return;
        }

        var icon = toast.Kind switch
        {
            ToastKind.Error => NotificationIcon.Error,
            ToastKind.Warning => NotificationIcon.Warning,
            _ => NotificationIcon.Info
        };

        try
        {
            Tray.ShowNotification(toast.Title, toast.Message ?? string.Empty, icon);
        }
        catch (Exception)
        {
            // Уведомление Windows не критично: то же сообщение остаётся во всплывающем окне приложения.
        }
    }

    private void OnTrayOpenClick(object sender, RoutedEventArgs e) => ShowFromTray();

    private void OnTrayExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnTrayDoubleClick(object sender, RoutedEventArgs e) => ShowFromTray();

    private void ShowFromTray()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
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
