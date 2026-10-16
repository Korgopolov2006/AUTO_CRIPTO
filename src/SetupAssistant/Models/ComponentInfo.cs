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

    public bool IsInstalled => Status is StatusLevel.Ok or StatusLevel.Warning;
}
