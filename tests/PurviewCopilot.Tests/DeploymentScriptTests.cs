using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Domain;
using PurviewCopilot.Infrastructure.Deployment;
using Xunit;

namespace PurviewCopilot.Tests;

public class DeploymentScriptTests
{
    private static ChangeOperation Operation(ModuleKey module, string control) =>
        new($"OP-{control}", module, OperationAction.Create, $"Purview/{module}/{control}", "{\"baseline\":\"recommended\"}", false, true);

    private static ChangeImpact Impact() =>
        new("exec", "tech", "biz", "user", "sec", "comp", "lic",
            new[] { "risk" }, new[] { "Re-run the assessment." }, new[] { "Restore the backup." });

    private static ChangePlan Plan(params ChangeOperation[] operations) =>
        new(Guid.NewGuid(), "tenant", DeploymentMode.GuidedDeployment, "Test plan", Array.Empty<string>(),
            operations, Impact(), DateTimeOffset.UtcNow, "tester", true);

    [Theory]
    [InlineData(ModuleKey.InformationProtection, "IPT-01", "New-Label")]
    [InlineData(ModuleKey.DataLossPrevention, "DLP-01", "New-DlpCompliancePolicy")]
    [InlineData(ModuleKey.Audit, "AUD-01", "Set-AdminAuditLogConfig")]
    [InlineData(ModuleKey.DataLifecycle, "DLM-01", "New-RetentionCompliancePolicy")]
    [InlineData(ModuleKey.RecordsManagement, "REC-01", "New-ComplianceTag")]
    public void Script_UsesTheRealCmdlet_ForMappedControls(ModuleKey module, string control, string expected)
    {
        var executor = new ChangeExecutor(allowLiveExecution: false);

        var script = executor.GenerateScript(Plan(Operation(module, control)));

        Assert.Contains(expected, script, StringComparison.Ordinal);
        Assert.Contains("Connect-IPPSSession", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_MarksUnmappedControlsAsManual_AndNeverGuesses()
    {
        var executor = new ChangeExecutor();

        var script = executor.GenerateScript(Plan(Operation(ModuleKey.Dspm, "DSP-01")));

        Assert.Contains("MANUAL DSP-01", script, StringComparison.Ordinal);
        Assert.Contains("no supported Security & Compliance cmdlet", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_IsIdempotent_AndDoesNotRemoveAnything()
    {
        var executor = new ChangeExecutor();

        var script = executor.GenerateScript(Plan(Operation(ModuleKey.SensitivityLabels, "SEN-01")));

        Assert.Contains("Get-Label", script, StringComparison.Ordinal);
        Assert.Contains("if (-not (Get-Label", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RollbackScript_ReversesInOrder_AndCommentsOutDestructiveSteps()
    {
        var executor = new ChangeExecutor();

        var script = executor.GenerateRollbackScript(Plan(
            Operation(ModuleKey.InformationProtection, "IPT-01"),
            Operation(ModuleKey.DataLossPrevention, "DLP-01")));

        Assert.Contains("ROLLBACK SCRIPT", script, StringComparison.Ordinal);
        Assert.Contains("# Remove-DlpCompliancePolicy", script, StringComparison.Ordinal);
        Assert.Contains("# Remove-Label", script, StringComparison.Ordinal);

        var dlp = script.IndexOf("Reverse OP-DLP-01", StringComparison.Ordinal);
        var label = script.IndexOf("Reverse OP-IPT-01", StringComparison.Ordinal);
        Assert.True(dlp >= 0 && label >= 0 && dlp < label, "Rollback must reverse the operation order.");
    }

    [Fact]
    public void ConfigurationPackage_RoundTripsOperations()
    {
        var plan = Plan(
            Operation(ModuleKey.InformationProtection, "IPT-01"),
            Operation(ModuleKey.DataLossPrevention, "DLP-01"));

        var json = ConfigurationPackage.Export(plan, DateTimeOffset.UtcNow);
        var imported = ConfigurationPackage.Import(json, DateTimeOffset.UtcNow);

        Assert.NotEqual(plan.Id, imported.Id);
        Assert.Equal(2, imported.Operations.Count);
        Assert.Equal("tenant", imported.TenantId);
        Assert.Contains("(imported)", imported.Title, StringComparison.Ordinal);
        Assert.Equal(plan.Operations[0].Target, imported.Operations[0].Target);
    }

    [Fact]
    public void ConfigurationPackage_RejectsPlanWithoutOperations()
    {
        var empty = Plan();
        var json = ConfigurationPackage.Export(empty, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => ConfigurationPackage.Import(json, DateTimeOffset.UtcNow));
    }
}
