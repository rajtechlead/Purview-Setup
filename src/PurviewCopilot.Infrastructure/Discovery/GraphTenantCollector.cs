using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Auth;
using PurviewCopilot.Domain;
using PurviewCopilot.Infrastructure.Auth;

namespace PurviewCopilot.Infrastructure.Discovery;

/// <summary>
/// Read-only Microsoft Graph discovery. Every call is individually fault-tolerant: a missing
/// permission degrades one fact and records a warning instead of failing the whole assessment.
/// Only GET requests are issued.
/// </summary>
public sealed class GraphTenantCollector : ITenantCollector
{
    private const string GlobalAdminRoleTemplateId = "62e90394-69f5-4237-9190-012177145e10";

    private readonly EntraTokenProvider _tokens;
    private readonly HttpClient _http;

    public GraphTenantCollector(EntraTokenProvider tokens, HttpClient? http = null)
    {
        _tokens = tokens;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    }

    public async Task<TenantSnapshot> CollectAsync(AuthConfiguration auth, CancellationToken cancellationToken = default)
    {
        var token = await _tokens.GetGraphTokenAsync(auth, cancellationToken).ConfigureAwait(false);
        var profile = CloudProfiles.For(auth.Cloud);
        var graph = profile.GraphBaseUrl;
        var graphBeta = profile.GraphBetaUrl;
        var warnings = new List<string>();
        var snapshot = new TenantSnapshot { CollectedUtc = DateTimeOffset.UtcNow };

        using (var organization = await GetJsonAsync($"{graph}/organization?$select=id,displayName", token, warnings, cancellationToken).ConfigureAwait(false))
        {
            if (organization is not null &&
                organization.RootElement.TryGetProperty("value", out var organizations) &&
                organizations.ValueKind == JsonValueKind.Array)
            {
                var first = organizations.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object)
                {
                    snapshot.TenantId = first.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty;
                    snapshot.TenantName = first.TryGetProperty("displayName", out var name) ? name.GetString() ?? string.Empty : string.Empty;
                }
            }
        }

        using (var domains = await GetJsonAsync($"{graph}/domains?$select=id,isInitial", token, warnings, cancellationToken).ConfigureAwait(false))
        {
            if (domains is not null)
            {
                foreach (var domain in domains.RootElement.GetProperty("value").EnumerateArray())
                {
                    if (domain.TryGetProperty("isInitial", out var initial) && initial.GetBoolean())
                    {
                        snapshot.TenantDomain = domain.GetProperty("id").GetString() ?? string.Empty;
                        break;
                    }
                }
            }
        }

        snapshot.UsersEnabled = await CountAsync($"{graph}/users?$filter=accountEnabled eq true&$top=1&$count=true", token, warnings, cancellationToken).ConfigureAwait(false);
        snapshot.Guests = await CountAsync($"{graph}/users?$filter=userType eq 'Guest'&$top=1&$count=true", token, warnings, cancellationToken).ConfigureAwait(false);
        snapshot.Groups = await CountAsync($"{graph}/groups?$top=1&$count=true", token, warnings, cancellationToken).ConfigureAwait(false);
        snapshot.ServicePrincipals = await CountAsync($"{graph}/servicePrincipals?$top=1&$count=true", token, warnings, cancellationToken).ConfigureAwait(false);
        snapshot.SharePointSites = await CountAsync($"{graph}/sites?$top=1&$count=true", token, warnings, cancellationToken).ConfigureAwait(false);
        snapshot.TeamsCount = await CountAsync($"{graph}/teams?$top=1&$count=true", token, warnings, cancellationToken).ConfigureAwait(false);

        snapshot.DevicesOnboarded = await CountAsync($"{graph}/deviceManagement/managedDevices?$top=1&$count=true", token, warnings, cancellationToken).ConfigureAwait(false);
        await ReadConditionalAccessAsync(snapshot, token, graphBeta, warnings, cancellationToken).ConfigureAwait(false);
        snapshot.DirectoryRoles = await CountAsync($"{graph}/directoryRoles?$top=1&$count=true", token, warnings, cancellationToken).ConfigureAwait(false);
        snapshot.DirectoryRoleAssignments = await CountAsync($"{graph}/roleManagement/directory/roleAssignments?$filter=assignmentType eq 'Assigned'&$top=1&$count=true", token, warnings, cancellationToken).ConfigureAwait(false);
        snapshot.PimEnabled = await CountAsync($"{graph}/roleManagement/directory/roleEligibilitySchedules?$top=1&$count=true", token, warnings, cancellationToken).ConfigureAwait(false) > 0;

        var mfaRegistered = await CountAsync($"{graph}/reports/authenticationMethods/userRegistrationDetails?$filter=isMfaRegistered eq true&$top=1&$count=true", token, warnings, cancellationToken).ConfigureAwait(false);
        if (mfaRegistered > 0 && snapshot.UsersEnabled > 0)
        {
            snapshot.MfaRegistrationPercent = Math.Round(mfaRegistered * 100.0 / snapshot.UsersEnabled, 1);
        }

        await ReadLicensesAsync(snapshot, token, graph, warnings, cancellationToken).ConfigureAwait(false);
        await ReadSecureScoreAsync(snapshot, token, graph, warnings, cancellationToken).ConfigureAwait(false);
        await ReadApplicationsAsync(snapshot, token, graph, warnings, cancellationToken).ConfigureAwait(false);
        await ReadServicePrincipalSecretsAsync(snapshot, token, graph, warnings, cancellationToken).ConfigureAwait(false);
        await ReadGlobalAdministratorsAsync(snapshot, token, graph, warnings, cancellationToken).ConfigureAwait(false);

        // Purview-specific configuration is read through Security & Compliance PowerShell.
        snapshot.ExchangeMailboxes = snapshot.UsersEnabled;
        warnings.Add("Mailbox count is approximated by enabled users; confirm with Get-Mailbox -ResultSize Unlimited.");

        snapshot.Warnings = warnings;
        return snapshot;
    }

    private async Task ReadLicensesAsync(TenantSnapshot snapshot, string token, string graph, List<string> warnings, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"{graph}/subscribedSkus?$select=skuPartNumber,consumedUnits,prepaidUnits", token, warnings, cancellationToken).ConfigureAwait(false);
        if (document is null) return;

        var licenses = new List<LicenseSku>();
        foreach (var sku in document.RootElement.GetProperty("value").EnumerateArray())
        {
            var partNumber = sku.TryGetProperty("skuPartNumber", out var p) ? p.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(partNumber)) continue;

            var consumed = sku.TryGetProperty("consumedUnits", out var c) ? c.GetInt32() : 0;
            var enabled = sku.TryGetProperty("prepaidUnits", out var prepaid) && prepaid.TryGetProperty("enabled", out var e) ? e.GetInt32() : 0;

            licenses.Add(new LicenseSku(partNumber, consumed, enabled));
        }

        snapshot.Licenses = licenses;
        snapshot.CopilotLicenses = licenses
            .Where(l => l.PartNumber.Contains("COPILOT", StringComparison.OrdinalIgnoreCase))
            .Sum(l => l.Consumed);
    }

    private async Task ReadSecureScoreAsync(TenantSnapshot snapshot, string token, string graph, List<string> warnings, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"{graph}/security/secureScores?$top=1", token, warnings, cancellationToken).ConfigureAwait(false);
        if (document is null) return;

        if (!document.RootElement.TryGetProperty("value", out var scoreValues) || scoreValues.ValueKind != JsonValueKind.Array) return;

        var first = scoreValues.EnumerateArray().FirstOrDefault();
        if (first.ValueKind != JsonValueKind.Object) return;

        var current = first.TryGetProperty("currentScore", out var c) ? c.GetDouble() : 0;
        var max = first.TryGetProperty("maxScore", out var m) ? m.GetDouble() : 0;

        if (max > 0) snapshot.SecureScorePercent = Math.Round(current * 100.0 / max, 1);
    }

    private async Task ReadApplicationsAsync(TenantSnapshot snapshot, string token, string graph, List<string> warnings, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"{graph}/applications?$select=id,passwordCredentials&$top=999", token, warnings, cancellationToken).ConfigureAwait(false);
        if (document is null) return;

        var withSecrets = 0;
        foreach (var app in document.RootElement.GetProperty("value").EnumerateArray())
        {
            if (app.TryGetProperty("passwordCredentials", out var credentials) &&
                credentials.ValueKind == JsonValueKind.Array &&
                credentials.GetArrayLength() > 0)
            {
                withSecrets++;
            }
        }

        snapshot.AppRegistrationsWithSecrets = withSecrets;
    }

    private async Task ReadConditionalAccessAsync(TenantSnapshot snapshot, string token, string graphBeta, List<string> warnings, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            $"{graphBeta}/identity/conditionalAccess/policies?$select=id,state,grantControls&$top=999", token, warnings, cancellationToken).ConfigureAwait(false);
        if (document is null) return;

        if (!document.RootElement.TryGetProperty("value", out var policies) || policies.ValueKind != JsonValueKind.Array) return;

        var enabled = 0;
        var mfa = 0;
        foreach (var policy in policies.EnumerateArray())
        {
            var state = policy.TryGetProperty("state", out var s) ? s.GetString() : null;
            if (!string.Equals(state, "enabled", StringComparison.OrdinalIgnoreCase)) continue;

            enabled++;
            if (policy.TryGetProperty("grantControls", out var grant) &&
                grant.ValueKind == JsonValueKind.Object &&
                grant.TryGetProperty("builtInControls", out var controls) &&
                controls.ValueKind == JsonValueKind.Array &&
                controls.EnumerateArray().Any(c => string.Equals(c.GetString(), "mfa", StringComparison.OrdinalIgnoreCase)))
            {
                mfa++;
            }
        }

        snapshot.ConditionalAccessPolicies = policies.GetArrayLength();
        snapshot.ConditionalAccessPoliciesEnabled = enabled;
        snapshot.ConditionalAccessMfaPolicies = mfa;
    }

    private async Task ReadServicePrincipalSecretsAsync(TenantSnapshot snapshot, string token, string graph, List<string> warnings, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"{graph}/servicePrincipals?$select=id,passwordCredentials&$top=999", token, warnings, cancellationToken).ConfigureAwait(false);
        if (document is null) return;

        var withSecrets = 0;
        foreach (var principal in document.RootElement.GetProperty("value").EnumerateArray())
        {
            if (principal.TryGetProperty("passwordCredentials", out var credentials) &&
                credentials.ValueKind == JsonValueKind.Array && credentials.GetArrayLength() > 0)
            {
                withSecrets++;
            }
        }

        snapshot.ServicePrincipalsWithSecrets = withSecrets;
    }

    private async Task ReadGlobalAdministratorsAsync(TenantSnapshot snapshot, string token, string graph, List<string> warnings, CancellationToken cancellationToken)
    {
        using var roles = await GetJsonAsync(
            $"{graph}/directoryRoles?$filter=roleTemplateId eq '{GlobalAdminRoleTemplateId}'", token, warnings, cancellationToken).ConfigureAwait(false);

        if (roles is null) return;

        if (!roles.RootElement.TryGetProperty("value", out var roleValues) || roleValues.ValueKind != JsonValueKind.Array) return;

        var role = roleValues.EnumerateArray().FirstOrDefault();
        if (role.ValueKind != JsonValueKind.Object || !role.TryGetProperty("id", out var roleId)) return;

        snapshot.GlobalAdministrators = await CountAsync(
            $"{graph}/directoryRoles/{roleId.GetString()}/members?$top=1&$count=true", token, warnings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> CountAsync(string url, string token, List<string> warnings, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(url, token, warnings, cancellationToken).ConfigureAwait(false);
        if (document is null) return 0;

        if (document.RootElement.TryGetProperty("@odata.count", out var count))
        {
            return count.GetInt32();
        }

        return document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, string token, List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("ConsistencyLevel", "eventual");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "Graph {0} returned {1}: {2}", Shorten(url), (int)response.StatusCode, Shorten(body, 180)));
                return null;
            }

            return JsonDocument.Parse(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add($"Graph call failed for {Shorten(url)}: {ex.Message}");
            return null;
        }
    }

    private static string Shorten(string value, int max = 90) =>
        value.Length <= max ? value : value[..max] + "…";
}
