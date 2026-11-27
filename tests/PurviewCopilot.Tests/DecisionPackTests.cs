using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class DecisionPackTests
{
    private static Recommendation Recommendation(string id, Severity severity, ModuleKey module, params string[] licences) =>
        new(id, module, $"Fix {id}", "evidence", severity, "Low", "Low", licences,
            new[]
            {
                new ChangeOperation($"OP-{id}", module, OperationAction.Create, $"target/{id}", "{}", false, true)
            });

    private static ChangePlan Plan(params ChangeOperation[] operations) =>
        new(Guid.NewGuid(), "tenant", DeploymentMode.GuidedDeployment, "Plan", Array.Empty<string>(), operations,
            new ChangeImpact("", "", "", "", "", "", "", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
            DateTimeOffset.UtcNow, "alice", true);

    private static readonly Recommendation[] Recommendations =
    {
        Recommendation("critical", Severity.Critical, ModuleKey.DataLossPrevention, "E5"),
        Recommendation("high", Severity.High, ModuleKey.SensitivityLabels, "E5"),
        Recommendation("medium", Severity.Medium, ModuleKey.Audit),
        Recommendation("low", Severity.Low, ModuleKey.CopilotGovernance, "E5")
    };

    [Fact]
    public void OptionsEscalateFromCriticalOnlyToEverything()
    {
        var pack = DecisionPackBuilder.Build(Plan(), Recommendations);

        Assert.Equal(new[] { "minimum", "recommended", "enhanced" }, pack.Options.Select(o => o.Key));
        Assert.Single(pack.Option("minimum").Items);
        Assert.Equal(3, pack.Option("recommended").Items.Count);
        Assert.Equal(4, pack.Option("enhanced").Items.Count);
    }

    [Fact]
    public void DuplicateRecommendationsAreCountedOnce()
    {
        var duplicated = Recommendations.Concat(new[] { Recommendations[0] }).ToArray();

        var pack = DecisionPackBuilder.Build(Plan(), duplicated);

        Assert.Equal(4, pack.Option("enhanced").Items.Count);
    }

    [Fact]
    public void OwnersFollowTheModule()
    {
        var pack = DecisionPackBuilder.Build(Plan(
            new ChangeOperation("a", ModuleKey.DataLossPrevention, OperationAction.Create, "a", "{}", false, true),
            new ChangeOperation("b", ModuleKey.Audit, OperationAction.Update, "b", "{}", false, true)),
            Array.Empty<Recommendation>());

        Assert.Contains(pack.Owners, o => o.Module == "DataLossPrevention" && o.Role == "Data Security Administrator");
        Assert.Contains(pack.Owners, o => o.Module == "Audit" && o.Role == "Compliance Manager");
    }

    [Fact]
    public void CostsAppearOnlyWhenAPriceIsSupplied()
    {
        var without = DecisionPackBuilder.Build(Plan(), Recommendations);
        var with = DecisionPackBuilder.Build(
            Plan(), Recommendations, new Dictionary<string, decimal> { ["E5"] = 12.5m });

        Assert.All(without.Costs, c => Assert.Null(c.UnitPrice));
        var e5 = Assert.Single(with.Costs, c => c.Licence == "E5");
        Assert.Equal(12.5m, e5.UnitPrice);
        Assert.Equal(12.5m * e5.Recommendations, e5.Total);
    }

    [Fact]
    public void PhasesAndAcceptanceCoverTouchedPhases()
    {
        var pack = DecisionPackBuilder.Build(Plan(
            new ChangeOperation("a", ModuleKey.Audit, OperationAction.Create, "a", "{}", false, true)),
            Array.Empty<Recommendation>());

        Assert.Contains(pack.Phases, p => p.OperationCount == 1);
        Assert.Contains(pack.AcceptanceCriteria, c => c.Contains("Every operation in the plan", StringComparison.Ordinal));
        Assert.Contains(pack.AcceptanceCriteria, c => c.Contains("Audit and monitoring", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RollbackLimitsNameIrreversibleWork()
    {
        var pack = DecisionPackBuilder.Build(Plan(
            new ChangeOperation("drop", ModuleKey.DataLifecycle, OperationAction.Delete, "drop", "{}", true, true,
                Reversibility.Irreversible)),
            Array.Empty<Recommendation>());

        Assert.Contains(pack.RollbackLimits, r => r.Contains("drop", StringComparison.Ordinal));
        Assert.Contains(pack.RollbackLimits, r => r.Contains("deleted content", StringComparison.Ordinal));
    }

    [Fact]
    public void MarkdownExportCarriesEverySection()
    {
        var markdown = DecisionPackRenderer.RenderMarkdown(
            DecisionPackBuilder.Build(Plan(), Recommendations, new Dictionary<string, decimal> { ["E5"] = 10m }));

        foreach (var heading in new[] { "## Options", "## Architecture", "## Phases", "## Owners", "## Acceptance criteria", "## Costs", "## Rollback limits" })
        {
            Assert.Contains(heading, markdown, StringComparison.Ordinal);
        }

        Assert.Contains("| Recommended |", markdown, StringComparison.Ordinal);
        Assert.Contains("E5: 3 recommendation(s)", markdown, StringComparison.Ordinal);
    }
}
