using System.IO;
using System.Text.Json;
using ModernWpf;
using SetupAssistant.Services.Interfaces;

namespace SetupAssistant.Services;

/// <inheritdoc cref="IUserSettingsService"/>
public sealed class UserSettingsService : IUserSettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SetupAssistant", "user-settings.json");

    private sealed class Stored
    {
        public AppThemeMode Theme { get; set; } = AppThemeMode.System;
    }

    private AppThemeMode _theme;

    public UserSettingsService()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                _theme = JsonSerializer.Deserialize<Stored>(File.ReadAllText(SettingsPath))?.Theme ?? AppThemeMode.System;
            }
        }
        catch (Exception)
        {
            // Повреждённый файл настроек не мешает запуску: остаётся тема по умолчанию.
        }
    }

    public AppThemeMode Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            Save();
            ApplyTheme();
        }
    }

    public void ApplyTheme()
    {
        ThemeManager.Current.ApplicationTheme = _theme switch
        {
            AppThemeMode.Light => ApplicationTheme.Light,
            AppThemeMode.Dark => ApplicationTheme.Dark,
            _ => null
        };
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Stored { Theme = _theme }));
        }
        catch (Exception)
        {
            // Не удалось сохранить: тема действует до закрытия приложения.
        }
    }
}
