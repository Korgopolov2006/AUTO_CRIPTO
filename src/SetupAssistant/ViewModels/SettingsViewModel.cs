using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SetupAssistant.Models;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.ViewModels;

/// <summary>Настройки приложения: оформление, сведения о программе и текущая конфигурация (Config.json).</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notifications;
    private readonly IExtensionUpdateService _extensionUpdateService;
    private readonly IUserSettingsService _userSettings;
    private readonly ILoggingService _logger;
    private readonly ISupportBundleService _supportBundle;
    private readonly ISystemService _systemService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private AppConfig _config;

    [ObservableProperty]
    private IReadOnlyList<ExtensionUpdateInfo> _extensionUpdates = Array.Empty<ExtensionUpdateInfo>();

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } = new[]
    {
        new ThemeOption(AppThemeMode.System, "Как в Windows"),
        new ThemeOption(AppThemeMode.Light, "Светлая"),
        new ThemeOption(AppThemeMode.Dark, "Тёмная")
    };

    [ObservableProperty]
    private AppThemeMode _selectedTheme;

    public string ConfigSource => _configService.ConfigSource;

    public string LogFolder => Path.GetDirectoryName(_logger.CurrentLogFilePath) ?? string.Empty;

    public string RuntimeText => RuntimeInformation.FrameworkDescription + " · " + RuntimeInformation.OSDescription;

    public string ExecutablePath => Environment.ProcessPath ?? string.Empty;

    public SettingsViewModel(
        IConfigService configService,
        IDialogService dialogService,
        INotificationService notifications,
        IExtensionUpdateService extensionUpdateService,
        IUserSettingsService userSettings,
        ILoggingService logger,
        ISupportBundleService supportBundle,
        ISystemService systemService,
        INavigationService navigationService)
    {
        _navigationService = navigationService;
        _supportBundle = supportBundle;
        _systemService = systemService;
        _configService = configService;
        _dialogService = dialogService;
        _notifications = notifications;
        _extensionUpdateService = extensionUpdateService;
        _userSettings = userSettings;
        _logger = logger;
        _config = configService.Current;
        _selectedTheme = userSettings.Theme;
        _selectedScale = userSettings.UiScale;
    }

    partial void OnSelectedThemeChanged(AppThemeMode value) => _userSettings.Theme = value;

    public IReadOnlyList<ScaleOption> ScaleOptions { get; } = new[]
    {
        new ScaleOption(1.0, "100% — обычный"),
        new ScaleOption(1.15, "115%"),
        new ScaleOption(1.3, "130%"),
        new ScaleOption(1.5, "150% — крупный")
    };

    /// <summary>Масштаб всего интерфейса: помогает при плохом зрении и на мелких экранах.</summary>
    [ObservableProperty]
    private double _selectedScale = 1.0;

    partial void OnSelectedScaleChanged(double value) => _userSettings.UiScale = value;

    /// <summary>Показать приветственный мастер первого запуска ещё раз.</summary>
    [RelayCommand]
    private void ShowWelcome()
    {
        _userSettings.FirstRunCompleted = false;
        _navigationService.NavigateTo<FirstRunViewModel>();
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
    private void OpenConfigEditor() => _navigationService.NavigateTo<ConfigEditorViewModel>();

    [RelayCommand]
    private void OpenVersions() => _navigationService.NavigateTo<VersionsViewModel>();

    [RelayCommand]
    private void ReloadConfig()
    {
        Config = _configService.Load();
        _notifications.Show(ToastKind.Success, "Конфигурация перезагружена", "Config.json прочитан заново.");
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        if (Directory.Exists(LogFolder))
        {
            Process.Start("explorer.exe", LogFolder);
        }
    }

    /// <summary>
    /// Подсказка для ручной регистрации Континент-АП: список полей и форматов. Ничего не заполняет и не отправляет;
    /// программа регистрации открывается только если пользователь сам нажал «Открыть».
    /// </summary>
    [RelayCommand]
    private void ShowKontinentRegistrationHelp()
    {
        var kontinent = Config.Kontinent;
        var lines = kontinent.RegistrationFields.Select(f => $"• {f.Field}: {f.Format} (пример: {f.Example})");
        var message = "Заполните форму регистрации Континент-АП самостоятельно и отправьте её сами.\n\n" +
                      string.Join("\n", lines) +
                      "\n\nПрограмма регистрации откроется, только если вы нажмёте «Открыть».";

        if (!_dialogService.ConfirmChoice(message, "Регистрация Континент-АП", "Открыть программу регистрации", "Закрыть"))
        {
            return;
        }

        var program = Environment.ExpandEnvironmentVariables(kontinent.RegistrationProgramPath);
        if (!System.IO.File.Exists(program))
        {
            _dialogService.ShowWarning($"Программа регистрации не найдена: {program}. Континент-АП установлен?");
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(program) { UseShellExecute = true });
    }

    [RelayCommand]
    private Task CreateSupportBundleAsync() =>
        Helpers.SupportBundleWorkflow.RunAsync(_dialogService, _notifications, _supportBundle, _systemService);

    /// <summary>Сведения для обращения в поддержку: версия, система, пути.</summary>
    [RelayCommand]
    private void CopyAboutInfo()
    {
        var text = string.Join(Environment.NewLine, new[]
        {
            $"{Config.AppName}",
            $"Версия: {Config.AppVersion}",
            $"Среда: {RuntimeText}",
            $"Файл: {ExecutablePath}",
            $"Конфигурация: {ConfigSource}",
            $"Журнал: {_logger.CurrentLogFilePath}"
        });

        try
        {
            Clipboard.SetText(text);
            _notifications.Show(ToastKind.Info, "Скопировано", "Сведения о программе в буфере обмена.");
        }
        catch (COMException)
        {
            _dialogService.ShowWarning("Не удалось скопировать в буфер обмена: он занят другим приложением.");
        }
    }
}

/// <summary>Пункт выбора темы оформления.</summary>
public sealed record ThemeOption(AppThemeMode Value, string Title);

/// <summary>Пункт выбора масштаба интерфейса.</summary>
public sealed record ScaleOption(double Value, string Title);
