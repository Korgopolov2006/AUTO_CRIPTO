namespace SetupAssistant.Models;

/// <summary>Общий вывод по результатам диагностики: готово ли рабочее место и что требует внимания.</summary>
public sealed class ReadinessSummary
{
    public static ReadinessSummary Empty { get; } = new(Array.Empty<ComponentInfo>());

    public ReadinessSummary(IReadOnlyList<ComponentInfo> results)
    {
        Results = results;
        Problems = results.Where(c => c.IsProblem).ToList();
        Fixable = Problems.Where(c => c.CanFix).ToList();
        ErrorCount = Problems.Count(c => c.Status == StatusLevel.Error);
        WarningCount = Problems.Count(c => c.Status == StatusLevel.Warning);
        OkCount = results.Count(c => c.Status == StatusLevel.Ok);
    }

    public IReadOnlyList<ComponentInfo> Results { get; }

    /// <summary>Предупреждения и ошибки обязательных компонентов.</summary>
    public IReadOnlyList<ComponentInfo> Problems { get; }

    /// <summary>Проблемы, которые мастер умеет исправить установкой.</summary>
    public IReadOnlyList<ComponentInfo> Fixable { get; }

    public int ErrorCount { get; }
    public int WarningCount { get; }
    public int OkCount { get; }

    /// <summary>Шаги установки, нужные для исправления, без повторов.</summary>
    public IReadOnlyList<SetupStepKind> FixSteps =>
        Fixable.Select(c => c.FixStep!.Value).Distinct().ToList();

    /// <summary>Код выхода для режима --check: 0 — всё в порядке, 1 — есть предупреждения, 2 — есть ошибки.</summary>
    public int ExitCode => ErrorCount > 0 ? 2 : WarningCount > 0 ? 1 : 0;
}
