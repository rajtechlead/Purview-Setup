using PurviewCopilot.Domain;

namespace PurviewCopilot.Infrastructure.Deployment;

public enum ScriptAutomation
{
    Automated = 0,
    Manual
}

/// <summary>One operation's executable block, its pre-apply verification, and how it is applied.</summary>
public sealed record OperationScript(ScriptAutomation Automation, string Verification, string Script);

/// <summary>
/// Maps each control to the Security &amp; Compliance PowerShell that actually configures it.
/// Every block is idempotent and checks current state first. Controls with no reliable cmdlet are
/// emitted as explicit MANUAL steps rather than guessed commands, so the script never pretends to
/// automate something the service does not expose.
/// </summary>
public static class PurviewScriptLibrary
{
    public static OperationScript Resolve(ChangeOperation operation)
    {
        var control = ControlId(operation);

        return control switch
        {
            "IPT-01" => Automated(Verification(ModuleKey.InformationProtection), Label()),
            "SEN-01" => Automated(Verification(ModuleKey.SensitivityLabels), LabelTaxonomy()),
            "IPT-02" or "SEN-02" => Automated(Verification(ModuleKey.SensitivityLabels), LabelPolicy()),
            "AUT-01" or "AUT-02" => Automated(Verification(ModuleKey.AutoLabeling), AutoLabelPolicy()),
            "DLP-01" or "DLP-04" => Automated(Verification(ModuleKey.DataLossPrevention), DlpPolicy()),
            "DLP-02" => Automated(Verification(ModuleKey.DataLossPrevention), DlpEnforce()),
            "REC-01" => Automated(Verification(ModuleKey.RecordsManagement), RecordTag()),
            "DLM-01" or "DLM-02" => Automated(Verification(ModuleKey.DataLifecycle), RetentionPolicy()),
            "AUD-01" => Automated(Verification(ModuleKey.Audit), EnableAudit()),
            "AUD-02" => Automated(Verification(ModuleKey.Audit), AuditRetention()),
            _ => Manual(control, operation.Module)
        };
    }

    /// <summary>Object name a block creates or updates, used for rollback guidance.</summary>
    public static string? ManagedObject(string control) => control switch
    {
        "IPT-01" => "PGC-Confidential-Baseline",
        "SEN-01" => "PGC-Public-Baseline, PGC-General-Baseline, PGC-Confidential-Baseline, PGC-HighlyConfidential-Baseline",
        "IPT-02" or "SEN-02" => "PGC-Label-Policy-Baseline",
        "AUT-01" or "AUT-02" => "PGC-AutoLabel-Baseline",
        "DLP-01" or "DLP-04" => "PGC-DLP-Baseline",
        "DLP-02" => "PGC-DLP-Baseline",
        "REC-01" => "PGC-Record-Financial",
        "DLM-01" or "DLM-02" => "PGC-Retention-Baseline",
        "AUD-02" => "PGC-Audit-365",
        _ => null
    };

    public static string ControlId(ChangeOperation operation)
    {
        var parts = operation.Target.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : operation.Id;
    }

    public static string Verification(ModuleKey module) => module switch
    {
        ModuleKey.Audit => "Get-AdminAuditLogConfig; Get-UnifiedAuditLogRetentionPolicy",
        ModuleKey.InformationProtection or ModuleKey.SensitivityLabels => "Get-Label; Get-LabelPolicy",
        ModuleKey.AutoLabeling => "Get-AutoSensitivityLabelPolicy; Get-AutoSensitivityLabelRule",
        ModuleKey.DataLossPrevention or ModuleKey.EndpointDlp => "Get-DlpCompliancePolicy",
        ModuleKey.RecordsManagement => "Get-ComplianceTag",
        ModuleKey.DataLifecycle => "Get-RetentionCompliancePolicy; Get-RetentionComplianceRule",
        ModuleKey.InsiderRisk => "Get-InsiderRiskPolicy",
        ModuleKey.CommunicationCompliance => "Get-SupervisoryReviewPolicyV2",
        ModuleKey.EDIscovery => "Get-ComplianceCase",
        _ => "Get-Label; Get-DlpCompliancePolicy; Get-RetentionCompliancePolicy"
    };

    private static OperationScript Automated(string verification, string script) =>
        new(ScriptAutomation.Automated, verification, script.Trim());

    private static OperationScript Manual(string control, ModuleKey module)
    {
        var script =
            $"""
             # MANUAL — {control}: no supported Security & Compliance cmdlet is mapped for this control.
             # Complete it in the Purview portal, then attach the evidence to {control}.
             Write-Host "MANUAL {control}: portal or tenant-specific configuration required."
             """;

        return new OperationScript(ScriptAutomation.Manual, Verification(module), script.Trim());
    }

    private static string Label() =>
        """
        $labelName = "PGC-Confidential-Baseline"
        if (-not (Get-Label -Identity $labelName -ErrorAction SilentlyContinue)) {
            New-Label -Name $labelName -DisplayName "Confidential (PGC baseline)" `
                -Tooltip "Baseline label applied by Purview Governance Copilot" `
                -Comment "Created from control IPT-01; review encryption and content marking before publishing."
        } else {
            Write-Host "Label $labelName already exists; no change."
        }
        """;

    private static string LabelTaxonomy() =>
        """
        $labels = @(
            @{ Name = "PGC-Public-Baseline"; Display = "Public (PGC baseline)" },
            @{ Name = "PGC-General-Baseline"; Display = "General (PGC baseline)" },
            @{ Name = "PGC-Confidential-Baseline"; Display = "Confidential (PGC baseline)" },
            @{ Name = "PGC-HighlyConfidential-Baseline"; Display = "Highly Confidential (PGC baseline)" }
        )
        foreach ($label in $labels) {
            if (-not (Get-Label -Identity $label.Name -ErrorAction SilentlyContinue)) {
                New-Label -Name $label.Name -DisplayName $label.Display -Comment "Four-level baseline taxonomy from control SEN-01."
            }
        }
        """;

    private static string LabelPolicy() =>
        """
        $policyName = "PGC-Label-Policy-Baseline"
        if (-not (Get-LabelPolicy -Identity $policyName -ErrorAction SilentlyContinue)) {
            New-LabelPolicy -Name $policyName -Labels @("PGC-Public-Baseline","PGC-General-Baseline","PGC-Confidential-Baseline","PGC-HighlyConfidential-Baseline") `
                -ExchangeLocation All -SharePointLocation All -OneDriveLocation All
        } else {
            Write-Host "Label policy $policyName already exists; no change."
        }
        """;

    private static string AutoLabelPolicy() =>
        """
        $policyName = "PGC-AutoLabel-Baseline"
        if (-not (Get-AutoSensitivityLabelPolicy -Identity $policyName -ErrorAction SilentlyContinue)) {
            New-AutoSensitivityLabelPolicy -Name $policyName -ExchangeLocation All -SharePointLocation All -OneDriveLocation All `
                -ApplySensitivityLabel "PGC-Confidential-Baseline"
            New-AutoSensitivityLabelRule -Name "PGC-AutoLabel-Financial" -Policy $policyName -Workload Exchange `
                -ContentContainsSensitiveInformation @(@{ Name = "Credit Card Number"; minCount = "1" }) `
                -ApplySensitivityLabel "PGC-Confidential-Baseline"
        } else {
            Write-Host "Auto-label policy $policyName already exists; no change."
        }
        """;

    private static string DlpPolicy() =>
        """
        $policyName = "PGC-DLP-Baseline"
        if (-not (Get-DlpCompliancePolicy -Identity $policyName -ErrorAction SilentlyContinue)) {
            New-DlpCompliancePolicy -Name $policyName -ExchangeLocation All -SharePointLocation All -OneDriveLocation All `
                -Mode TestWithNotifications
            New-DlpComplianceRule -Name "PGC-DLP-PII" -Policy $policyName `
                -ContentContainsSensitiveInformation @(@{ Name = "Credit Card Number"; minCount = "1" }, @{ Name = "U.S. Social Security Number (SSN)"; minCount = "1" }) `
                -NotifyUser Owner -BlockAccess $false
        } else {
            Write-Host "DLP policy $policyName already exists; no change."
        }
        """;

    private static string DlpEnforce() =>
        """
        $policyName = "PGC-DLP-Baseline"
        if (Get-DlpCompliancePolicy -Identity $policyName -ErrorAction SilentlyContinue) {
            Set-DlpCompliancePolicy -Identity $policyName -Mode Enable
        } else {
            Write-Warning "Create the baseline DLP policy before enforcing it (control DLP-01)."
        }
        """;

    private static string RecordTag() =>
        """
        $tagName = "PGC-Record-Financial"
        if (-not (Get-ComplianceTag -Identity $tagName -ErrorAction SilentlyContinue)) {
            New-ComplianceTag -Name $tagName -RetentionAction Keep -RetentionDuration 2555 `
                -Comment "Financial record baseline from control REC-01; confirm duration with Legal."
        } else {
            Write-Host "Retention label $tagName already exists; no change."
        }
        """;

    private static string RetentionPolicy() =>
        """
        $policyName = "PGC-Retention-Baseline"
        if (-not (Get-RetentionCompliancePolicy -Identity $policyName -ErrorAction SilentlyContinue)) {
            New-RetentionCompliancePolicy -Name $policyName -ExchangeLocation All -SharePointLocation All -OneDriveLocation All
        }
        if (-not (Get-RetentionComplianceRule -Identity "PGC-Retention-7Y" -Policy $policyName -ErrorAction SilentlyContinue)) {
            New-RetentionComplianceRule -Name "PGC-Retention-7Y" -Policy $policyName -RetentionDuration 2555 -RetentionComplianceAction Keep
        }
        """;

    private static string EnableAudit() =>
        """
        $config = Get-AdminAuditLogConfig
        if (-not $config.UnifiedAuditLogIngestionEnabled) {
            Set-AdminAuditLogConfig -UnifiedAuditLogIngestionEnabled $true
        } else {
            Write-Host "Unified audit log is already enabled."
        }
        """;

    private static string AuditRetention() =>
        """
        $policyName = "PGC-Audit-365"
        if (-not (Get-UnifiedAuditLogRetentionPolicy -Identity $policyName -ErrorAction SilentlyContinue)) {
            New-UnifiedAuditLogRetentionPolicy -Name $policyName -RetentionDuration 365 -Priority 100 `
                -RecordTypes ExchangeAdmin,AzureActiveDirectory,SharePoint,OneDrive
        } else {
            Write-Host "Audit retention policy $policyName already exists; no change."
        }
        """;
}
