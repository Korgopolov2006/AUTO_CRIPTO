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
        public double UiScale { get; set; } = 1.0;
        public bool FirstRunCompleted { get; set; }
    }

    private readonly Stored _stored = new();

    public event EventHandler? UiScaleChanged;

    public UserSettingsService()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                _stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(SettingsPath)) ?? new Stored();
            }
        }
        catch (Exception)
        {
            // Повреждённый файл настроек не мешает запуску: остаются значения по умолчанию.
        }

        _stored.UiScale = Math.Clamp(_stored.UiScale, 1.0, 1.5);
    }

    public AppThemeMode Theme
    {
        get => _stored.Theme;
        set
        {
            _stored.Theme = value;
            Save();
            ApplyTheme();
        }
    }

    public double UiScale
    {
        get => _stored.UiScale;
        set
        {
            _stored.UiScale = Math.Clamp(value, 1.0, 1.5);
            Save();
            UiScaleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool FirstRunCompleted
    {
        get => _stored.FirstRunCompleted;
        set
        {
            _stored.FirstRunCompleted = value;
            Save();
        }
    }

    public void ApplyTheme()
    {
        ThemeManager.Current.ApplicationTheme = _stored.Theme switch
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
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_stored));
        }
        catch (Exception)
        {
            // Не удалось сохранить: настройка действует до закрытия приложения.
        }
    }
}
