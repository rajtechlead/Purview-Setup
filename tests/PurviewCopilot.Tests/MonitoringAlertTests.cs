using PurviewCopilot.Application.Monitoring;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class MonitoringAlertTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private static AssessmentResult Result(double overall, params Finding[] findings) =>
        new(Guid.NewGuid(), "tenant", At, "AssessmentOnly",
            new ScoreSet(overall, overall, overall, overall, MaturityLevel.Defined, 10, overall, overall, overall,
                Array.Empty<ModuleScore>()),
            findings, Array.Empty<Recommendation>(), 49);

    private static Finding Failing(string id, Severity severity) =>
        new(id, ModuleKey.Audit, $"title {id}", FindingStatus.Fail, severity, "evidence", "remediation", Array.Empty<string>());

    [Fact]
    public void Drift_ProducesAnAlertWithSeverityAndAction()
    {
        var drift = new[]
        {
            new DriftItem("DRF-001", DriftKind.Security, "Unified audit log was disabled",
                "True", "False", Severity.Critical, "Re-enable audit immediately.")
        };

        var alerts = AlertBuilder.Build(null, null, drift, At);

        var alert = Assert.Single(alerts);
        Assert.Equal("ALERT-DRF-001", alert.Id);
        Assert.Equal(Severity.Critical, alert.Severity);
        Assert.Contains("Re-enable audit", alert.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(70, 64, Severity.High)]
    [InlineData(70, 55, Severity.Critical)]
    public void ScoreDrop_RaisesAnAlertAtTheRightSeverity(double previous, double current, Severity expected)
    {
        var alerts = AlertBuilder.Build(Result(current), Result(previous), Array.Empty<DriftItem>(), At);

        var alert = Assert.Single(alerts);
        Assert.Equal("ALERT-SCORE-DROP", alert.Id);
        Assert.Equal(expected, alert.Severity);
    }

    [Fact]
    public void SmallScoreDrop_DoesNotAlert()
    {
        var alerts = AlertBuilder.Build(Result(69), Result(70), Array.Empty<DriftItem>(), At);

        Assert.Empty(alerts);
    }

    [Fact]
    public void CriticalFailures_AlertWithTheControlIds()
    {
        var current = Result(60, Failing("AUD-01", Severity.Critical), Failing("DLP-01", Severity.Critical), Failing("CPM-01", Severity.Medium));

        var alerts = AlertBuilder.Build(current, null, Array.Empty<DriftItem>(), At);

        var alert = Assert.Single(alerts);
        Assert.Equal("ALERT-CRITICAL-FAILURES", alert.Id);
        Assert.Contains("2 critical", alert.Title, StringComparison.Ordinal);
        Assert.Contains("AUD-01", alert.Detail, StringComparison.Ordinal);
        Assert.Contains("DLP-01", alert.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("CPM-01", alert.Detail, StringComparison.Ordinal);
    }
}
