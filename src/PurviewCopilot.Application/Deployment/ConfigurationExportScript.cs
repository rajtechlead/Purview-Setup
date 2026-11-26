namespace PurviewCopilot.Application.Deployment;

/// <summary>
/// Read-only export of the tenant's existing Purview objects. The generated script only calls
/// Get- cmdlets and writes a JSON document; it never creates, edits or removes configuration.
/// </summary>
public static class ConfigurationExportScript
{
    public const string Script = """
        param(
            [Parameter(Mandatory=$true)][string]$AppId,
            [Parameter(Mandatory=$true)][string]$Thumbprint,
            [Parameter(Mandatory=$true)][string]$Organization,
            [Parameter(Mandatory=$true)][string]$OutputPath,
            [string]$ConnectionUri,
            [string]$AuthorizationUri
        )

        $ErrorActionPreference = 'Stop'

        $export = [ordered]@{
            exportedUtc = (Get-Date).ToUniversalTime().ToString('o')
            organization = $Organization
            labels = @()
            labelPolicies = @()
            autoLabelRules = @()
            autoLabelPolicies = @()
            dlpPolicies = @()
            dlpRules = @()
            retentionPolicies = @()
            retentionRules = @()
            complianceTags = @()
            errors = @()
        }

        function Capture([string]$name, [scriptblock]$query) {
            try {
                $export[$name] = @(& $query)
            } catch {
                $export.errors += "$name`: $($_.Exception.Message)"
            }
        }

        Import-Module ExchangeOnlineManagement -ErrorAction Stop

        $connect = @{ AppId = $AppId; CertificateThumbprint = $Thumbprint; Organization = $Organization }
        if (-not [string]::IsNullOrWhiteSpace($ConnectionUri)) { $connect.ConnectionUri = $ConnectionUri }
        if (-not [string]::IsNullOrWhiteSpace($AuthorizationUri)) { $connect.AzureADAuthorizationEndpointUri = $AuthorizationUri }
        Connect-IPPSSession @connect -ErrorAction Stop | Out-Null

        try {
            Capture 'labels' { Get-Label }
            Capture 'labelPolicies' { Get-LabelPolicy }
            Capture 'autoLabelRules' { Get-AutoSensitivityLabelRule }
            Capture 'autoLabelPolicies' { Get-AutoSensitivityLabelPolicy }
            Capture 'dlpPolicies' { Get-DlpCompliancePolicy }
            Capture 'dlpRules' { Get-DlpComplianceRule }
            Capture 'retentionPolicies' { Get-RetentionCompliancePolicy }
            Capture 'retentionRules' { Get-RetentionComplianceRule }
            Capture 'complianceTags' { Get-ComplianceTag }

            $export | ConvertTo-Json -Depth 20 | Set-Content -Path $OutputPath -Encoding UTF8
        }
        finally {
            Disconnect-ExchangeOnline -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
        }

        Write-Host "Exported existing Purview configuration to $OutputPath."
        """;

    /// <summary>Wraps the export in a script block and supplies its mandatory parameters.</summary>
    public static string BuildInvocation(
        string appId,
        string thumbprint,
        string organization,
        string outputPath,
        string? connectionUri = null,
        string? authorizationUri = null)
    {
        var cloud = string.IsNullOrWhiteSpace(connectionUri)
            ? string.Empty
            : $" -ConnectionUri '{Escape(connectionUri)}'";

        if (!string.IsNullOrWhiteSpace(authorizationUri))
            cloud += $" -AuthorizationUri '{Escape(authorizationUri)}'";

        return $"& {{\n{Script}\n}} -AppId '{Escape(appId)}' -Thumbprint '{Escape(thumbprint)}' " +
               $"-Organization '{Escape(organization)}' -OutputPath '{Escape(outputPath)}'{cloud}";
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
