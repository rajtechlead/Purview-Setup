using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class DeploymentModePolicyTests
{
    [Fact]
    public void AssessmentOnly_CannotBuildAPlan()
    {
        Assert.False(DeploymentModePolicy.AllowsPlan(DeploymentMode.AssessmentOnly));
        Assert.All(Enum.GetValues<DeploymentMode>().Where(m => m != DeploymentMode.AssessmentOnly),
            mode => Assert.True(DeploymentModePolicy.AllowsPlan(mode)));
    }

    [Fact]
    public void Modes_SelectDifferentStartingSets()
    {
        Assert.True(DeploymentModePolicy.Preselect(DeploymentMode.GuidedDeployment, "DLP-01", Severity.High, false));
        Assert.False(DeploymentModePolicy.Preselect(DeploymentMode.GuidedDeployment, "DLP-01", Severity.Low, false));
        Assert.False(DeploymentModePolicy.Preselect(DeploymentMode.Custom, "DLP-01", Severity.Critical, false));
        Assert.True(DeploymentModePolicy.Preselect(DeploymentMode.Expert, "DLP-01", Severity.Low, false));
        Assert.False(DeploymentModePolicy.Preselect(DeploymentMode.AssessmentOnly, "DLP-01", Severity.Critical, false));
    }

    [Fact]
    public void Greenfield_OnlySelectsFoundations_OnAnEmptyTenant()
    {
        Assert.True(DeploymentModePolicy.Preselect(DeploymentMode.Greenfield, "SEN-01", Severity.Low, greenfield: true));
        Assert.False(DeploymentModePolicy.Preselect(DeploymentMode.Greenfield, "SEN-01", Severity.Low, greenfield: false));
        Assert.False(DeploymentModePolicy.Preselect(DeploymentMode.Greenfield, "UX-06", Severity.Critical, greenfield: true));
    }

    [Fact]
    public void OnlyOneClick_BuildsAutomatically()
    {
        Assert.True(DeploymentModePolicy.RequiresAutomaticPlan(DeploymentMode.OneClickRecommended));
        Assert.False(DeploymentModePolicy.RequiresAutomaticPlan(DeploymentMode.GuidedDeployment));
        Assert.False(DeploymentModePolicy.RequiresAutomaticPlan(DeploymentMode.Expert));
    }
}
