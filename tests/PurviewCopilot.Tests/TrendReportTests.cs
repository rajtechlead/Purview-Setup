using PurviewCopilot.Application.Reporting;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class TrendReportTests
{
    [Fact]
    public void Build_OrdersOldestToNewest_AndLimitsTheSeries()
    {
        var history = new[]
        {
            Result(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), 70),
            Result(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 50),
            Result(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), 60)
        };

        var points = TrendReport.Build(history, take: 2);

        Assert.Equal(2, points.Count);
        Assert.Equal(60, points[0].Overall);
        Assert.Equal(70, points[1].Overall);
    }

    [Fact]
    public void Markdown_ContainsDirectionAndSeries()
    {
        var history = new[]
        {
            Result(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 50),
            Result(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), 65)
        };

        var markdown = TrendReport.BuildMarkdown(
            "Contoso", "tenant-1", history, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains("Governance trend — Contoso", markdown, StringComparison.Ordinal);
        Assert.Contains("Overall: 50 → 65 (+15 points)", markdown, StringComparison.Ordinal);
        Assert.Contains("| 2026-01-01 00:00 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| 2026-02-01 00:00 |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyHistory_IsReportedNotCrashed()
    {
        var markdown = TrendReport.BuildMarkdown("Contoso", "tenant-1", Array.Empty<AssessmentResult>(), DateTimeOffset.UtcNow);

        Assert.Contains("No stored assessments", markdown, StringComparison.Ordinal);
    }

    private static AssessmentResult Result(DateTimeOffset at, double overall) =>
        new(Guid.NewGuid(), "tenant-1", at, "AssessmentOnly",
            new ScoreSet(overall, overall - 5, overall - 10, overall - 15, MaturityLevel.Managed, 100 - overall,
                overall, overall, overall, Array.Empty<ModuleScore>()),
            Array.Empty<Finding>(), Array.Empty<Recommendation>(), 49);
}
