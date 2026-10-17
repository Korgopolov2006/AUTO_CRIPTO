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
        ILoggingService logger)
    {
        _configService = configService;
        _dialogService = dialogService;
        _notifications = notifications;
        _extensionUpdateService = extensionUpdateService;
        _userSettings = userSettings;
        _logger = logger;
        _config = configService.Current;
        _selectedTheme = userSettings.Theme;
    }

    partial void OnSelectedThemeChanged(AppThemeMode value) => _userSettings.Theme = value;

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
