using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SetupAssistant.Models;

namespace SetupAssistant.ViewModels;

/// <summary>Основа экранов с последовательными шагами (настройка, удаление): список шагов и индикатор выполнения.</summary>
public abstract partial class StepsViewModelBase : ViewModelBase
{
    public ObservableCollection<SetupStepModel> Steps { get; } = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private int _completedStepsCount;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _currentStepText = string.Empty;

    public int TotalStepsCount => Steps.Count;

    /// <summary>Подпись индикатора до запуска и после завершения.</summary>
    protected abstract string IdleText { get; }

    /// <summary>Пересчитывает индикатор по текущему состоянию шагов; вызывается на старте и при каждом изменении шага.</summary>
    protected virtual void OnProgressUpdated()
    {
    }

    protected void UpdateProgress()
    {
        var total = Steps.Count;
        var done = Steps.Count(s => s.Status is StatusLevel.Ok or StatusLevel.Warning or StatusLevel.Error or StatusLevel.Skipped);
        CompletedStepsCount = done;
        ProgressPercent = total == 0 ? 0 : done * 100 / total;
        OnPropertyChanged(nameof(TotalStepsCount));

        var active = Steps.FirstOrDefault(s => s.Status == StatusLevel.InProgress);
        CurrentStepText = active is not null
            ? $"Шаг {done + 1} из {total}: {active.Title}"
            : IsRunning
                ? "Подготовка…"
                : done > 0 && done == total ? "Все шаги выполнены" : IdleText;

        OnProgressUpdated();
    }
}
