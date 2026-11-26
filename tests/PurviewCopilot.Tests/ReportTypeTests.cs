using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Reporting;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class ReportTypeTests
{
    [Fact]
    public void Executive_OmitsDetailedFindingsAndRecommendations()
    {
        var html = new HtmlReportExporter().Render(Context(ReportType.Executive));

        Assert.Contains("Executive report", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<h2>Findings</h2>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<h2>Recommendations</h2>", html, StringComparison.Ordinal);
        Assert.Contains("Overall", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Security_KeepsOnlyHighSeverityFindings()
    {
        var html = new HtmlReportExporter().Render(Context(ReportType.Security));

        Assert.Contains("High finding", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Low finding", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<h2>Recommendations</h2>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Remediation_IncludesOpenFindingsAndRecommendations()
    {
        var markdown = new MarkdownReportExporter().Render(Context(ReportType.Remediation));

        Assert.Contains("## Findings", markdown, StringComparison.Ordinal);
        Assert.Contains("## Recommendations", markdown, StringComparison.Ordinal);
        Assert.Contains("High finding", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Passing finding", markdown, StringComparison.Ordinal);
    }

    private static ReportContext Context(ReportType type)
    {
        var findings = new[]
        {
            new Finding("F1", ModuleKey.Audit, "High finding", FindingStatus.Fail, Severity.High, "e", "r", new[] { "ISO" }),
            new Finding("F2", ModuleKey.Audit, "Low finding", FindingStatus.Warning, Severity.Low, "e", "r", Array.Empty<string>()),
            new Finding("F3", ModuleKey.Audit, "Passing finding", FindingStatus.Pass, Severity.Medium, "e", "r", Array.Empty<string>())
        };

        var recommendations = new[]
        {
            new Recommendation("R1", ModuleKey.Audit, "Enable audit", "why", Severity.High, "S", "Low",
                Array.Empty<string>(), Array.Empty<ChangeOperation>())
        };

        var scores = new ScoreSet(60, 55, 60, 65, MaturityLevel.Defined, 40, 50, 50, 60, Array.Empty<ModuleScore>());
        var assessment = new AssessmentResult(Guid.NewGuid(), "tenant", DateTimeOffset.UtcNow, "AssessmentOnly",
            scores, findings, recommendations, 49);

        return new ReportContext(new TenantSnapshot { TenantId = "tenant", TenantName = "Contoso" },
            assessment, "Contoso", "tester", DateTimeOffset.UtcNow,
            new[] { new FrameworkCoverage("ISO", 50, 10, 5, new[] { "A" }) }, type);
    }
}
