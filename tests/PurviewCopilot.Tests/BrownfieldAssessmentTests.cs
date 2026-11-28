using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class BrownfieldAssessmentTests
{
    [Fact]
    public void EmptyTenantIsNotBrownfield()
    {
        var result = BrownfieldAssessmentBuilder.Build(new TenantSnapshot { TenantId = "contoso" });

        Assert.False(result.IsBrownfield);
        Assert.Empty(result.ConfiguredAreas);
        Assert.Empty(result.Debt);
        Assert.Empty(result.Waves);
    }

    [Fact]
    public void ConfiguredTenantIsBrownfieldWithNamedAreas()
    {
        var snapshot = new TenantSnapshot
        {
            TenantId = "contoso",
            UnifiedAuditEnabled = true,
            SensitivityLabelCount = 4,
            LabelPolicyCount = 1,
            DlpPolicyCount = 2,
            DlpEnforcedCount = 2,
            EndpointDlpEnabled = true,
            RetentionPolicyCount = 1,
            RetentionCoversAllLocations = true
        };

        var result = BrownfieldAssessmentBuilder.Build(snapshot);

        Assert.True(result.IsBrownfield);
        Assert.Contains("Sensitivity labels", result.ConfiguredAreas);
        Assert.Contains("DLP", result.ConfiguredAreas);
        Assert.Empty(result.Debt);
        Assert.Empty(result.Waves);
    }

    [Fact]
    public void AuditOffWhilePurviewIsConfiguredIsCriticalDebt()
    {
        var snapshot = new TenantSnapshot
        {
            TenantId = "contoso",
            SensitivityLabelCount = 3,
            DlpPolicyCount = 1,
            DlpEnforcedCount = 1
        };

        var result = BrownfieldAssessmentBuilder.Build(snapshot);

        var audit = Assert.Single(result.Debt, d => d.Id == "BRD-01");
        Assert.Equal(Severity.Critical, audit.Severity);
        Assert.Equal("Wave 1 — stop exposure", result.Waves[0].Name);
        Assert.Contains(audit, result.Waves[0].Items);
    }

    [Fact]
    public void PartialConfigurationProducesDebtInIncreasingWaves()
    {
        var snapshot = new TenantSnapshot
        {
            TenantId = "contoso",
            UnifiedAuditEnabled = true,
            SensitivityLabelCount = 5,
            LabelPolicyCount = 0,
            TrainableClassifierCount = 2,
            AutoLabelingRuleCount = 0,
            DlpPolicyCount = 3,
            DlpEnforcedCount = 0,
            EndpointDlpEnabled = false,
            RetentionPolicyCount = 2,
            RetentionCoversAllLocations = false,
            RetentionLabelCount = 1,
            DispositionReviewEnabled = false,
            EdiscoveryCaseCount = 1,
            EdiscoveryPremiumEnabled = false
        };

        var result = BrownfieldAssessmentBuilder.Build(snapshot);

        Assert.Equal(new[] { "BRD-02", "BRD-04", "BRD-05", "BRD-06", "BRD-07", "BRD-08", "BRD-09" },
            result.Debt.Select(d => d.Id));
        Assert.Equal(3, result.Waves.Count);
        Assert.All(result.Waves[0].Items, d => Assert.Equal(Severity.Critical, d.Severity));
        Assert.All(result.Waves[2].Items, d => Assert.True(d.Severity < Severity.High));

        var scheduled = result.Waves.SelectMany(w => w.Items).Select(d => d.Id).OrderBy(id => id).ToArray();
        Assert.Equal(result.Debt.Select(d => d.Id).OrderBy(id => id), scheduled);
    }

    [Fact]
    public void TestModeOnlyIsCriticalWhenNothingEnforces()
    {
        var allTest = BrownfieldAssessmentBuilder.Build(new TenantSnapshot
        {
            UnifiedAuditEnabled = true,
            DlpPolicyCount = 2,
            DlpEnforcedCount = 0
        });

        var someEnforce = BrownfieldAssessmentBuilder.Build(new TenantSnapshot
        {
            UnifiedAuditEnabled = true,
            DlpPolicyCount = 2,
            DlpEnforcedCount = 1
        });

        Assert.Contains(allTest.Debt, d => d.Id == "BRD-02" && d.Severity == Severity.Critical);
        Assert.DoesNotContain(allTest.Debt, d => d.Id == "BRD-03");
        Assert.Contains(someEnforce.Debt, d => d.Id == "BRD-03" && d.Severity == Severity.High);
        Assert.DoesNotContain(someEnforce.Debt, d => d.Id == "BRD-02");
    }

    [Fact]
    public void BrownfieldModePreselectsCriticalAndHighOnly()
    {
        Assert.True(DeploymentModePolicy.Preselect(
            DeploymentMode.Brownfield, "DLP-01", Severity.Critical, greenfield: false));
        Assert.True(DeploymentModePolicy.Preselect(
            DeploymentMode.Brownfield, "DLP-01", Severity.High, greenfield: false));
        Assert.False(DeploymentModePolicy.Preselect(
            DeploymentMode.Brownfield, "DLP-01", Severity.Medium, greenfield: false));
        Assert.Contains("three incremental waves",
            DeploymentModePolicy.Describe(DeploymentMode.Brownfield, greenfield: false), StringComparison.Ordinal);
    }
}
