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
}
