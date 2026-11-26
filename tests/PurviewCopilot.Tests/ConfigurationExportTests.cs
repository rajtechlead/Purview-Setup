using System.Text.Json;
using PurviewCopilot.Application.Deployment;
using Xunit;

namespace PurviewCopilot.Tests;

public class ConfigurationExportTests
{
    [Fact]
    public void ArmTemplate_IsValidJson_AndTargetsPurviewAccount()
    {
        using var document = JsonDocument.Parse(InfrastructureTemplates.Arm);
        var resource = document.RootElement.GetProperty("resources")[0];

        Assert.Equal("Microsoft.Purview/accounts", resource.GetProperty("type").GetString());
        Assert.Equal("2021-07-01", resource.GetProperty("apiVersion").GetString());
        Assert.Equal("SystemAssigned", resource.GetProperty("identity").GetProperty("type").GetString());
    }

    [Fact]
    public void BicepAndTerraform_DeclareThePurviewAccount()
    {
        Assert.Contains("Microsoft.Purview/accounts@2021-07-01", InfrastructureTemplates.Bicep, StringComparison.Ordinal);
        Assert.Contains("azurerm_purview_account", InfrastructureTemplates.Terraform, StringComparison.Ordinal);
        Assert.Contains("SystemAssigned", InfrastructureTemplates.Terraform, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportScript_ReadsTheExpectedObjectSets()
    {
        foreach (var cmdlet in new[]
                 {
                     "Get-Label", "Get-LabelPolicy", "Get-AutoSensitivityLabelRule", "Get-AutoSensitivityLabelPolicy",
                     "Get-DlpCompliancePolicy", "Get-DlpComplianceRule", "Get-RetentionCompliancePolicy",
                     "Get-RetentionComplianceRule", "Get-ComplianceTag"
                 })
        {
            Assert.Contains(cmdlet + " ", ConfigurationExportScript.Script, StringComparison.Ordinal);
        }

        Assert.Contains("ConvertTo-Json -Depth 20", ConfigurationExportScript.Script, StringComparison.Ordinal);
        Assert.Contains("Disconnect-ExchangeOnline", ConfigurationExportScript.Script, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportScript_IsReadOnly()
    {
        foreach (var mutation in new[]
                 {
                     "Set-Label", "New-Label", "Remove-Label", "Set-DlpCompliancePolicy",
                     "New-DlpCompliancePolicy", "Remove-DlpCompliancePolicy", "New-RetentionCompliancePolicy",
                     "Set-RetentionCompliancePolicy", "Remove-RetentionCompliancePolicy", "Set-ComplianceTag"
                 })
        {
            Assert.DoesNotContain(mutation, ConfigurationExportScript.Script, StringComparison.Ordinal);
        }
    }
}
