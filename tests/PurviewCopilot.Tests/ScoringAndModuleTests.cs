using PurviewCopilot.Application.Demo;
using PurviewCopilot.Application.Modules;
using PurviewCopilot.Domain;
using PurviewCopilot.Domain.Scoring;
using Xunit;

namespace PurviewCopilot.Tests;

public class ScoringAndModuleTests
{
    private static Finding Make(string id, ModuleKey module, FindingStatus status, Severity severity = Severity.High) =>
        new(id, module, id, status, severity, "evidence", "remediation", Array.Empty<string>());

    [Fact]
    public void Compute_WeightsPassAndWarningPerModule()
    {
        var findings = new[]
        {
            Make("AUD-01", ModuleKey.Audit, FindingStatus.Pass),
            Make("AUD-02", ModuleKey.Audit, FindingStatus.Warning),
            Make("AUD-03", ModuleKey.Audit, FindingStatus.Fail)
        };

        var result = ScoringEngine.Compute(findings);
        var audit = result.Modules.Single(m => m.Module == ModuleKey.Audit);

        Assert.Equal(3, audit.Assessed);
        Assert.Equal(53.33, audit.Score, 2);
        Assert.Equal(1, audit.Passed);
        Assert.Equal(1, audit.Warnings);
        Assert.Equal(1, audit.Failed);
    }

    [Fact]
    public void Compute_IgnoresNotAssessed_WhenScoring()
    {
        var findings = new[]
        {
            Make("AUD-01", ModuleKey.Audit, FindingStatus.Pass),
            Make("AUD-02", ModuleKey.Audit, FindingStatus.NotAssessed)
        };

        var audit = ScoringEngine.Compute(findings).Modules.Single(m => m.Module == ModuleKey.Audit);
        Assert.Equal(1, audit.Assessed);
        Assert.Equal(100, audit.Score, 2);
    }

    [Fact]
    public void Compute_Empty_ReturnsZeroScores()
    {
        var scores = ScoringEngine.Compute(Array.Empty<Finding>());
        Assert.Equal(0, scores.Overall, 2);
        Assert.Equal(MaturityLevel.Initial, scores.MaturityLevel);
    }

    [Fact]
    public void Risk_IncreasesWithSeverity_AndIsCapped()
    {
        var low = ScoringEngine.ComputeRisk(new[] { Make("X", ModuleKey.Audit, FindingStatus.Fail, Severity.Low) });
        var critical = ScoringEngine.ComputeRisk(new[] { Make("Y", ModuleKey.Audit, FindingStatus.Fail, Severity.Critical) });

        Assert.True(critical > low);
        Assert.Equal(100, ScoringEngine.ComputeRisk(Enumerable.Range(0, 20)
            .Select(i => Make($"F{i}", ModuleKey.Audit, FindingStatus.Fail, Severity.Critical))), 2);
    }

    [Fact]
    public void Warning_ContributesLessRiskThanFailure()
    {
        var warning = ScoringEngine.ComputeRisk(new[] { Make("W", ModuleKey.Audit, FindingStatus.Warning, Severity.High) });
        var failure = ScoringEngine.ComputeRisk(new[] { Make("F", ModuleKey.Audit, FindingStatus.Fail, Severity.High) });

        Assert.True(warning < failure);
    }

    [Fact]
    public void Catalog_CoversEveryModule_WithAtLeastTwoControls()
    {
        // Custom is supplied by external plugins, so it has no static catalog entry.
        foreach (var module in Enum.GetValues<ModuleKey>().Where(m => m != ModuleKey.Custom))
        {
            var rules = ModuleCatalog.All.Where(r => r.Module == module).ToList();
            Assert.True(rules.Count >= 2, $"{module} has only {rules.Count} control(s).");
        }

        Assert.True(ModuleCatalog.All.Count >= 45);

        // Findings are keyed by control id, so a duplicate id would merge two controls' remediation.
        var duplicates = ModuleCatalog.All.GroupBy(r => r.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Catalog_EveryRuleEvaluatesWithoutThrowing()
    {
        var snapshot = DemoTenant.Create();

        foreach (var rule in ModuleCatalog.All)
        {
            var finding = rule.ToFinding(snapshot);
            Assert.NotEqual(FindingStatus.NotAssessed, finding.Status);
            Assert.False(string.IsNullOrWhiteSpace(finding.Title));
            Assert.False(string.IsNullOrWhiteSpace(finding.Remediation));
        }
    }

    [Fact]
    public void Registry_ExposesSixteenModules_AndWeightsArePositive()
    {
        var registry = new ModuleRegistry();
        Assert.Equal(16, registry.Modules.Count);
        Assert.All(registry.Weights.Values, weight => Assert.True(weight > 0));
    }
}
