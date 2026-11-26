using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Auth;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Infrastructure.Discovery;

/// <summary>
/// Reads Purview configuration with app-only Security &amp; Compliance PowerShell. Certificate
/// authentication is required because the flow is non-interactive. The script is read-only and
/// emits JSON; a failed cmdlet degrades one fact instead of aborting the collection.
/// </summary>
public sealed class CompliancePowerShellCollector
{
    private readonly string _scriptDirectory;

    public CompliancePowerShellCollector(string scriptDirectory)
    {
        _scriptDirectory = scriptDirectory;
        Directory.CreateDirectory(scriptDirectory);
    }

    public async Task<bool> EnrichAsync(TenantSnapshot snapshot, AuthConfiguration auth, CancellationToken cancellationToken = default)
    {
        if (auth.Mode != AuthMode.Certificate)
        {
            AddWarning(snapshot, "Purview configuration was not collected: app-only certificate authentication is required for Security & Compliance PowerShell.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(snapshot.TenantDomain))
        {
            AddWarning(snapshot, "Purview configuration was not collected: the tenant's initial domain could not be resolved.");
            return false;
        }

        var scriptPath = Path.Combine(_scriptDirectory, "Get-PurviewFacts.ps1");
        var outputPath = Path.Combine(_scriptDirectory, $"purview-facts-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(scriptPath, Script, cancellationToken).ConfigureAwait(false);

        var cloud = CloudProfiles.For(auth.Cloud);
        var cloudArguments = cloud.ComplianceConnectionUri is null
            ? string.Empty
            : $" -ConnectionUri \"{cloud.ComplianceConnectionUri}\" -AuthorizationUri \"{cloud.ComplianceAuthorizationUri}\"";

        var arguments =
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\" " +
            $"-AppId \"{auth.ClientId}\" -Thumbprint \"{auth.CertificateThumbprint}\" " +
            $"-Organization \"{snapshot.TenantDomain}\" -OutputPath \"{outputPath}\"" + cloudArguments;

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            AddWarning(snapshot, "Security & Compliance PowerShell could not be started.");
            return false;
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            AddWarning(snapshot,
                $"Security & Compliance collection failed (exit {process.ExitCode}). {Truncate(stderr.Length > 0 ? stderr : stdout)}");
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath, cancellationToken).ConfigureAwait(false));
            Apply(snapshot, document.RootElement);
            return true;
        }
        catch (Exception ex)
        {
            AddWarning(snapshot, $"Purview facts could not be parsed: {ex.Message}");
            return false;
        }
        finally
        {
            try { File.Delete(outputPath); } catch (IOException) { /* best effort */ }
        }
    }

    private static void Apply(TenantSnapshot snapshot, JsonElement root)
    {
        snapshot.SensitivityLabelCount = GetInt(root, "labelCount");
        snapshot.LabelPolicyCount = GetInt(root, "labelPolicyCount");
        snapshot.AutoLabelingRuleCount = GetInt(root, "autoLabelRuleCount");
        snapshot.DlpPolicyCount = GetInt(root, "dlpPolicyCount");
        snapshot.DlpEnforcedCount = GetInt(root, "dlpEnforcedCount");
        snapshot.EndpointDlpEnabled = GetBool(root, "endpointDlpEnabled");
        snapshot.RetentionPolicyCount = GetInt(root, "retentionPolicyCount");
        snapshot.RetentionLabelCount = GetInt(root, "retentionLabelCount");
        snapshot.RetentionCoversAllLocations = GetBool(root, "retentionAllLocations");
        snapshot.InsiderRiskPolicyCount = GetInt(root, "insiderRiskPolicyCount");
        snapshot.CommunicationCompliancePolicyCount = GetInt(root, "communicationCompliancePolicyCount");
        snapshot.EdiscoveryCaseCount = GetInt(root, "ediscoveryCaseCount");
        snapshot.EdiscoveryPremiumEnabled = GetBool(root, "ediscoveryPremium");
        snapshot.AuditAlertPolicyCount = GetInt(root, "auditAlertPolicyCount");
        snapshot.AuditRetentionDays = GetInt(root, "auditRetentionDays");

        if (root.TryGetProperty("auditEnabled", out var auditEnabled) && auditEnabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            snapshot.UnifiedAuditEnabled = auditEnabled.GetBoolean();
        }

        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
        {
            foreach (var error in errors.EnumerateArray())
            {
                AddWarning(snapshot, $"Purview check skipped: {error.GetString()}");
            }
        }
    }

    private static int GetInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    private static bool GetBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private static void AddWarning(TenantSnapshot snapshot, string warning)
    {
        var warnings = snapshot.Warnings.ToList();
        warnings.Add(warning);
        snapshot.Warnings = warnings;
    }

    private static string Truncate(string value) => value.Length <= 400 ? value.Trim() : value[..400].Trim() + "…";

    /// <summary>Read-only collection script. No Set-, New-, Remove- or Enable- cmdlets are permitted here.</summary>
    public const string Script =
        """
        param(
            [Parameter(Mandatory=$true)][string]$AppId,
            [Parameter(Mandatory=$true)][string]$Thumbprint,
            [Parameter(Mandatory=$true)][string]$Organization,
            [Parameter(Mandatory=$true)][string]$OutputPath,
            [string]$ConnectionUri,
            [string]$AuthorizationUri
        )

        $ErrorActionPreference = 'Stop'
        $result = [ordered]@{
            labelCount = 0
            labelPolicyCount = 0
            autoLabelRuleCount = 0
            dlpPolicyCount = 0
            dlpEnforcedCount = 0
            endpointDlpEnabled = $false
            retentionPolicyCount = 0
            retentionLabelCount = 0
            retentionAllLocations = $false
            insiderRiskPolicyCount = 0
            communicationCompliancePolicyCount = 0
            ediscoveryCaseCount = 0
            ediscoveryPremium = $false
            auditAlertPolicyCount = 0
            auditRetentionDays = 0
            auditEnabled = $false
            errors = @()
        }

        function Add-Error([string]$name, [string]$message) {
            $result.errors += "$name`: $message"
        }

        Import-Module ExchangeOnlineManagement -ErrorAction Stop

        $connect = @{ AppId = $AppId; CertificateThumbprint = $Thumbprint; Organization = $Organization }
        if (-not [string]::IsNullOrWhiteSpace($ConnectionUri)) { $connect.ConnectionUri = $ConnectionUri }
        if (-not [string]::IsNullOrWhiteSpace($AuthorizationUri)) { $connect.AzureADAuthorizationEndpointUri = $AuthorizationUri }
        Connect-IPPSSession @connect -ErrorAction Stop | Out-Null

        try { $result.labelCount = @(Get-Label).Count } catch { Add-Error 'labels' $_.Exception.Message }
        try { $result.labelPolicyCount = @(Get-LabelPolicy).Count } catch { Add-Error 'labelPolicies' $_.Exception.Message }
        try { $result.autoLabelRuleCount = @(Get-AutoSensitivityLabelRule).Count } catch { Add-Error 'autoLabelRules' $_.Exception.Message }

        try {
            $policies = @(Get-DlpCompliancePolicy)
            $result.dlpPolicyCount = $policies.Count
            $result.dlpEnforcedCount = @($policies | Where-Object { $_.Mode -eq 'Enable' }).Count
            $result.endpointDlpEnabled = @($policies | Where-Object { @($_.EndpointDlpLocation).Count -gt 0 }).Count -gt 0
        } catch { Add-Error 'dlp' $_.Exception.Message }

        try {
            $retention = @(Get-RetentionCompliancePolicy)
            $result.retentionPolicyCount = $retention.Count
            $allLocations = $retention | Where-Object {
                @($_.ExchangeLocation) -contains 'All' -and @($_.SharePointLocation) -contains 'All'
            }
            $result.retentionAllLocations = @($allLocations).Count -gt 0
        } catch { Add-Error 'retention' $_.Exception.Message }

        try { $result.retentionLabelCount = @(Get-ComplianceTag).Count } catch { Add-Error 'retentionLabels' $_.Exception.Message }

        try { $result.insiderRiskPolicyCount = @(Get-InsiderRiskPolicy).Count } catch { Add-Error 'insiderRisk' $_.Exception.Message }
        try { $result.communicationCompliancePolicyCount = @(Get-SupervisoryReviewPolicyV2).Count } catch { Add-Error 'communicationCompliance' $_.Exception.Message }

        try {
            $cases = @(Get-ComplianceCase)
            $result.ediscoveryCaseCount = $cases.Count
            $result.ediscoveryPremium = @($cases | Where-Object { $_.CaseType -eq 'AdvancedEdiscovery' }).Count -gt 0
        } catch { Add-Error 'ediscovery' $_.Exception.Message }

        try {
            $result.auditAlertPolicyCount = @(Get-ProtectionAlert).Count
        } catch { Add-Error 'auditAlerts' $_.Exception.Message }

        try {
            $config = Get-AdminAuditLogConfig
            $result.auditEnabled = [bool]$config.UnifiedAuditLogIngestionEnabled
        } catch { Add-Error 'auditConfig' $_.Exception.Message }

        try {
            $policies = @(Get-UnifiedAuditLogRetentionPolicy)
            $maxDays = 0
            foreach ($policy in $policies) {
                $duration = [string]$policy.RetentionDuration
                if (-not [string]::IsNullOrWhiteSpace($duration)) {
                    $days = [int]($duration.Split('.')[0])
                    if ($days -gt $maxDays) { $maxDays = $days }
                }
            }
            $result.auditRetentionDays = $maxDays
        } catch { Add-Error 'auditRetention' $_.Exception.Message }

        $result | ConvertTo-Json -Depth 5 | Set-Content -Path $OutputPath -Encoding UTF8
        Disconnect-ExchangeOnline -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
        """;
}
