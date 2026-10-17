using CommunityToolkit.Mvvm.ComponentModel;

namespace SetupAssistant.Models;

/// <summary>Результат проверки одного компонента рабочего места (ПО, драйвера, расширения и т.д.).</summary>
public partial class ComponentInfo : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private ComponentType _type;

    [ObservableProperty]
    private StatusLevel _status = StatusLevel.NotChecked;

    [ObservableProperty]
    private string? _installedVersion;

    [ObservableProperty]
    private string? _minimumVersion;

    [ObservableProperty]
    private string? _recommendedVersion;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Группа в списке диагностики.</summary>
    public string Category { get; set; } = "Прочее";

    public int CategoryOrder => ComponentCatalog.CategoryOrder(Category);

    /// <summary>Шаг мастера, который устраняет проблему с этим компонентом; null — автоматического исправления нет.</summary>
    public SetupStepKind? FixStep { get; set; }

    /// <summary>Необязательный компонент: его отсутствие не считается проблемой рабочего места.</summary>
    public bool IsOptional { get; set; }

    /// <summary>Предупреждение или ошибка, о которых нужно сообщить пользователю.</summary>
    public bool IsProblem => !IsOptional && Status is StatusLevel.Warning or StatusLevel.Error;

    public bool CanFix => IsProblem && FixStep is not null;

    public string StatusText => Status switch
    {
        StatusLevel.Ok => "Готово",
        StatusLevel.Warning => "Предупреждение",
        StatusLevel.Error => "Ошибка",
        StatusLevel.InProgress => "Выполняется",
        StatusLevel.Skipped => "Пропущено",
        _ => "Не проверено"
    };

    /// <summary>Текст для буфера обмена: имя, статус, версии и описание проблемы.</summary>
    public string ToClipboardText() =>
        $"{Name} — {StatusText}" +
        (string.IsNullOrEmpty(InstalledVersion) ? string.Empty : $"; версия: {InstalledVersion}") +
        (string.IsNullOrEmpty(RecommendedVersion) ? string.Empty : $"; рекомендуется: {RecommendedVersion}") +
        (string.IsNullOrEmpty(ErrorMessage) ? string.Empty : $"\n{ErrorMessage}");

    public bool IsInstalled => Status is StatusLevel.Ok or StatusLevel.Warning;
}
