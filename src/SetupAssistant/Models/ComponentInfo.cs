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
    [NotifyPropertyChangedFor(nameof(DetailsText))]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsText))]
    private string? _errorMessage;

    /// <summary>Текст для таблицы диагностики: описание проблемы, а если её нет — пояснение, что за компонент.</summary>
    public string DetailsText => string.IsNullOrEmpty(ErrorMessage) ? Description : ErrorMessage;

    /// <summary>Группа в списке диагностики.</summary>
    public string Category { get; set; } = "Прочее";

    public int CategoryOrder => ComponentCatalog.CategoryOrder(Category);

    /// <summary>Шаг мастера, который устраняет проблему с этим компонентом; null — автоматического исправления нет.</summary>
    public SetupStepKind? FixStep { get; set; }

    /// <summary>Необязательный компонент: его отсутствие не считается проблемой рабочего места.</summary>
    public bool IsOptional { get; set; }

    /// <summary>Предупреждение или ошибка, о которых нужно сообщить пользователю.</summary>
    public bool IsProblem => !IsOptional && Status is StatusLevel.Warning or StatusLevel.Error;

    /// <summary>Компонент не установлен или не подключён: это сведения о рабочем месте, а не проблема.</summary>
    public bool IsMissing => Status == StatusLevel.Info && FixStep is not null && !IsOptional;

    /// <summary>Мастер может установить или исправить компонент.</summary>
    public bool CanFix => (IsProblem || IsMissing) && FixStep is not null;

    /// <summary>Подпись для статуса «К сведению»: «Не установлено», «Не подключён», «Не в домене».</summary>
    public string? InfoText { get; set; }

    /// <summary>Показывать в обзоре на главной странице.</summary>
    public bool ShowOnDashboard { get; set; }

    /// <summary>Совет «что делать» для компонента, который не установлен или требует внимания; null — совет не нужен.</summary>
    public string? Advice => Helpers.HelpAdvisor.ForComponent(this);

    /// <summary>Это проверка «Компьютер в домене / MDM».</summary>
    public bool IsDeviceManagement => Name.StartsWith("Управление компьютером", StringComparison.Ordinal);

    public string StatusText => Status switch
    {
        StatusLevel.Ok => "Готово",
        StatusLevel.Warning => "Предупреждение",
        StatusLevel.Error => "Ошибка",
        StatusLevel.InProgress => "Выполняется",
        StatusLevel.Skipped => "Пропущено",
        StatusLevel.Info => string.IsNullOrEmpty(InfoText) ? "К сведению" : InfoText,
        _ => "Не проверено"
    };

    /// <summary>Короткая строка для обзора на главной: «Установлено, версия X», «Не установлено», «Компьютер в домене».</summary>
    public string DashboardText => Status switch
    {
        StatusLevel.Ok when Type == ComponentType.Token => $"Подключён: {InstalledVersion}",
        StatusLevel.Ok when IsDeviceManagement => "Компьютер в домене или управляется (MDM)",
        StatusLevel.Ok => string.IsNullOrEmpty(InstalledVersion) ? "Установлено" : $"Установлено, версия {InstalledVersion}",
        StatusLevel.Info when IsDeviceManagement => "Компьютер не в домене и не управляется (MDM)",
        StatusLevel.Info => string.IsNullOrEmpty(InfoText) ? "К сведению" : InfoText,
        StatusLevel.Warning or StatusLevel.Error => string.IsNullOrEmpty(ErrorMessage) ? StatusText : ErrorMessage,
        _ => StatusText
    };

    /// <summary>Текст для буфера обмена: имя, статус, версии и описание проблемы.</summary>
    public string ToClipboardText() =>
        $"{Name} — {StatusText}" +
        (string.IsNullOrEmpty(InstalledVersion) ? string.Empty : $"; версия: {InstalledVersion}") +
        (string.IsNullOrEmpty(RecommendedVersion) ? string.Empty : $"; рекомендуется: {RecommendedVersion}") +
        (string.IsNullOrEmpty(ErrorMessage) ? string.Empty : $"\n{ErrorMessage}");

    public bool IsInstalled => Status is StatusLevel.Ok or StatusLevel.Warning;
}
