namespace SetupAssistant.Services.Interfaces;

public enum AppThemeMode
{
    System,
    Light,
    Dark
}

/// <summary>Пользовательские настройки внешнего вида; хранятся в профиле пользователя.</summary>
public interface IUserSettingsService
{
    AppThemeMode Theme { get; set; }

    /// <summary>Применяет сохранённую тему к приложению.</summary>
    void ApplyTheme();
}
