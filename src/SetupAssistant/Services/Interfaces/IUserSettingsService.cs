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

    /// <summary>Масштаб интерфейса: 1.0 — обычный, 1.15 / 1.3 / 1.5 — крупнее (для плохого зрения и мелких экранов).</summary>
    double UiScale { get; set; }

    /// <summary>Мастер первого запуска уже показывали.</summary>
    bool FirstRunCompleted { get; set; }

    /// <summary>Изменился масштаб интерфейса.</summary>
    event EventHandler? UiScaleChanged;

    /// <summary>Применяет сохранённую тему к приложению.</summary>
    void ApplyTheme();
}
