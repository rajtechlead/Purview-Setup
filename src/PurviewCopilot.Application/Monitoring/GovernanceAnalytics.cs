using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Monitoring;

/// <summary>One cell of the risk heatmap: open findings for a module at a severity.</summary>
public sealed record HeatmapCell(ModuleKey Module, Severity Severity, int Count);

/// <summary>One point in the score history, ordered oldest to newest.</summary>
public sealed record TrendPoint(
    DateTimeOffset RunUtc,
    double Overall,
    double Security,
    double Governance,
    double Compliance,
    double Risk,
    MaturityLevel Maturity);

public static class RiskHeatmap
{
    /// <summary>
    /// Groups open (fail or warning) findings by module and severity. Passing and unassessed
    /// controls are excluded so the grid shows only work that needs attention.
    /// </summary>
    public static IReadOnlyList<HeatmapCell> Build(IEnumerable<Finding> findings) =>
        findings
            .Where(f => f.Status is FindingStatus.Fail or FindingStatus.Warning)
            .GroupBy(f => (f.Module, f.Severity))
            .Select(g => new HeatmapCell(g.Key.Module, g.Key.Severity, g.Count()))
            .OrderBy(c => c.Module)
            .ThenByDescending(c => c.Severity)
            .ToList();
}

public static class TrendSeries
{
    /// <summary>Projects stored assessments into an ascending series, keeping the newest points.</summary>
    public static IReadOnlyList<TrendPoint> Build(IEnumerable<AssessmentResult> history, int take = 12) =>
        history
            .OrderBy(a => a.RunUtc)
            .TakeLast(take)
            .Select(a => new TrendPoint(
                a.RunUtc,
                a.Scores.Overall,
                a.Scores.Security,
                a.Scores.Governance,
                a.Scores.Compliance,
                a.Scores.Risk,
                a.Scores.MaturityLevel))
            .ToList();
}
