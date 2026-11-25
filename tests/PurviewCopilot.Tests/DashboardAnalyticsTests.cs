using PurviewCopilot.Application.Monitoring;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class DashboardAnalyticsTests
{
    private static Finding Finding(string id, ModuleKey module, FindingStatus status, Severity severity) =>
        new(id, module, $"title {id}", status, severity, "evidence", "remediation", Array.Empty<string>());

    private static AssessmentResult Result(DateTimeOffset at, double overall) =>
        new(Guid.NewGuid(), "tenant", at, "AssessmentOnly",
            new ScoreSet(overall, overall, overall, overall, MaturityLevel.Defined, 5, overall, overall, overall,
                Array.Empty<ModuleScore>()),
            Array.Empty<Finding>(), Array.Empty<Recommendation>(), 49);

    [Fact]
    public void Heatmap_GroupsOpenFindingsAndIgnoresPassingControls()
    {
        var findings = new[]
        {
            Finding("AUD-01", ModuleKey.Audit, FindingStatus.Fail, Severity.Critical),
            Finding("AUD-02", ModuleKey.Audit, FindingStatus.Fail, Severity.Critical),
            Finding("AUD-03", ModuleKey.Audit, FindingStatus.Warning, Severity.High),
            Finding("DLP-01", ModuleKey.DataLossPrevention, FindingStatus.Fail, Severity.Medium),
            Finding("DLP-02", ModuleKey.DataLossPrevention, FindingStatus.Pass, Severity.Critical),
            Finding("DLP-03", ModuleKey.DataLossPrevention, FindingStatus.NotAssessed, Severity.High)
        };

        var cells = RiskHeatmap.Build(findings);

        Assert.Equal(3, cells.Count);
        Assert.Contains(cells, c => c.Module == ModuleKey.Audit && c.Severity == Severity.Critical && c.Count == 2);
        Assert.Contains(cells, c => c.Module == ModuleKey.Audit && c.Severity == Severity.High && c.Count == 1);
        Assert.Contains(cells, c => c.Module == ModuleKey.DataLossPrevention && c.Severity == Severity.Medium && c.Count == 1);
        Assert.DoesNotContain(cells, c => c.Severity == Severity.Critical && c.Module == ModuleKey.DataLossPrevention);
    }

    [Fact]
    public void Trend_IsAscending_AndKeepsTheNewestPoints()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var history = Enumerable.Range(0, 15)
            .Select(i => Result(start.AddDays(i), 40 + i))
            .Reverse()
            .ToList();

        var trend = TrendSeries.Build(history, 12);

        Assert.Equal(12, trend.Count);
        Assert.Equal(start.AddDays(3), trend[0].RunUtc);
        Assert.Equal(start.AddDays(14), trend[^1].RunUtc);
        Assert.Equal(43, trend[0].Overall);
        Assert.Equal(54, trend[^1].Overall);
        Assert.True(trend.Zip(trend.Skip(1)).All(pair => pair.First.RunUtc < pair.Second.RunUtc));
    }
}
