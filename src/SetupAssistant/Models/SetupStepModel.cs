using CommunityToolkit.Mvvm.ComponentModel;

namespace SetupAssistant.Models;

/// <summary>Один шаг мастера настройки рабочего места с индикацией прогресса.</summary>
public partial class SetupStepModel : ObservableObject
{
    [ObservableProperty]
    private SetupStepKind _kind;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private StatusLevel _status = StatusLevel.NotChecked;

    [ObservableProperty]
    private string? _details;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>Шаг участвует в запуске (для шагов установки пользователь может снять флажок).</summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>Пользователь может менять <see cref="IsSelected"/> (только у шагов установки и только до запуска).</summary>
    [ObservableProperty]
    private bool _isSelectable;

    /// <summary>Длинное описание шага развёрнуто.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Шаг завершился, но для окончания установки нужна перезагрузка.</summary>
    [ObservableProperty]
    private bool _needsReboot;

    /// <summary>Для шагов проверки: шаг установки, к которому относится проверка.</summary>
    public SetupStepKind? PairedInstallKind { get; set; }

    public bool HasLongDetails => (Details?.Length ?? 0) > 110;

    /// <summary>Совет «что делать», если шаг завершился предупреждением или ошибкой.</summary>
    public string? Advice => AdviceOverride ?? Helpers.HelpAdvisor.ForStep(Kind, Status, Details);

    private string? _adviceOverride;

    /// <summary>Собственный совет шага (например, для теста подписи); заменяет совет по умолчанию.</summary>
    public string? AdviceOverride
    {
        get => _adviceOverride;
        set
        {
            if (SetProperty(ref _adviceOverride, value))
            {
                OnPropertyChanged(nameof(Advice));
                OnPropertyChanged(nameof(HasAdvice));
            }
        }
    }

    public bool HasAdvice => Advice is not null;

    partial void OnDetailsChanged(string? value)
    {
        OnPropertyChanged(nameof(HasLongDetails));
        OnPropertyChanged(nameof(Advice));
        OnPropertyChanged(nameof(HasAdvice));
    }

    /// <summary>Шаг можно запустить заново: он завершился проблемой.</summary>
    public bool CanRetry => IsSelectable && Status is StatusLevel.Warning or StatusLevel.Error;

    partial void OnStatusChanged(StatusLevel value)
    {
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(Advice));
        OnPropertyChanged(nameof(HasAdvice));
    }

    /// <summary>Идентификатор объекта шага (например, имя удаляемого продукта из конфигурации).</summary>
    public string? Key { get; set; }
}
