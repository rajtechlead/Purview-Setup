using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class GreenfieldBaselineTests
{
    [Fact]
    public void EmptyTenant_IsGreenfield()
    {
        Assert.True(GreenfieldBaseline.IsGreenfield(new TenantSnapshot { TenantId = "t" }));
    }

    [Fact]
    public void AnyExistingPurviewConfiguration_IsNotGreenfield()
    {
        var labelled = new TenantSnapshot { TenantId = "t", SensitivityLabelCount = 1 };
        var dlp = new TenantSnapshot { TenantId = "t", DlpPolicyCount = 1 };
        var retention = new TenantSnapshot { TenantId = "t", RetentionPolicyCount = 1 };

        Assert.False(GreenfieldBaseline.IsGreenfield(labelled));
        Assert.False(GreenfieldBaseline.IsGreenfield(dlp));
        Assert.False(GreenfieldBaseline.IsGreenfield(retention));
    }

    [Fact]
    public void FoundationRecommendations_AreOrderedControls()
    {
        Assert.True(GreenfieldBaseline.IsFoundation("SEN-01"));
        Assert.True(GreenfieldBaseline.IsFoundation("dlp-01"));
        Assert.False(GreenfieldBaseline.IsFoundation("UX-06"));
    }
}
