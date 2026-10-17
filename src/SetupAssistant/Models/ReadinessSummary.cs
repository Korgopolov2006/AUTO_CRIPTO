namespace SetupAssistant.Models;

/// <summary>Общий вывод по результатам диагностики: готово ли рабочее место и что требует внимания.</summary>
public sealed class ReadinessSummary
{
    public static ReadinessSummary Empty { get; } = new(Array.Empty<ComponentInfo>());

    public ReadinessSummary(IReadOnlyList<ComponentInfo> results)
    {
        Results = results;
        Problems = results.Where(c => c.IsProblem).ToList();
        Fixable = results.Where(c => c.CanFix).ToList();
        Missing = results.Where(c => c.IsMissing).ToList();
        InfoCount = results.Count(c => c.Status == StatusLevel.Info);
        ErrorCount = Problems.Count(c => c.Status == StatusLevel.Error);
        WarningCount = Problems.Count(c => c.Status == StatusLevel.Warning);
        OkCount = results.Count(c => c.Status == StatusLevel.Ok);
    }

    public IReadOnlyList<ComponentInfo> Results { get; }

    /// <summary>Предупреждения и ошибки обязательных компонентов.</summary>
    public IReadOnlyList<ComponentInfo> Problems { get; }

    /// <summary>Что мастер умеет установить или исправить: недостающие компоненты и проблемы.</summary>
    public IReadOnlyList<ComponentInfo> Fixable { get; }

    /// <summary>Не установлено — к сведению, не проблема (не на каждом компьютере нужен весь набор).</summary>
    public IReadOnlyList<ComponentInfo> Missing { get; }

    /// <summary>Сколько проверок завершились сведениями: не установлено, не подключён, не в домене.</summary>
    public int InfoCount { get; }

    public int ErrorCount { get; }
    public int WarningCount { get; }
    public int OkCount { get; }

    /// <summary>Шаги установки, нужные для исправления, без повторов.</summary>
    public IReadOnlyList<SetupStepKind> FixSteps =>
        Fixable.Select(c => c.FixStep!.Value).Distinct().ToList();

    /// <summary>Код выхода для режима --check: 0 — всё в порядке, 1 — есть предупреждения, 2 — есть ошибки.</summary>
    public int ExitCode => ErrorCount > 0 ? 2 : WarningCount > 0 ? 1 : 0;
}
