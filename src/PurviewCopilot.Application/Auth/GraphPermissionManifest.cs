namespace PurviewCopilot.Application.Auth;

/// <summary>
/// The single source of truth for the app-only Microsoft Graph permissions the product needs.
/// Names are resolved to role ids against the tenant's Graph service principal at provisioning
/// time, so no permission GUID is hardcoded. Every entry is read-only by design.
/// </summary>
public sealed record GraphPermission(string Name, string Feature, string Reason);

public static class GraphPermissionManifest
{
    public const string MicrosoftGraphAppId = "00000003-0000-0000-c000-000000000000";

    public static IReadOnlyList<GraphPermission> Required { get; } = new GraphPermission[]
    {
        new("User.Read.All", "Directory", "User, guest and MFA registration counts."),
        new("Group.Read.All", "Directory", "Group counts for exposure and sharing checks."),
        new("Application.Read.All", "Directory", "App registrations, service principals and credential expiry."),
        new("Organization.Read.All", "Directory", "Tenant identity and subscribed license SKUs."),
        new("Domain.Read.All", "Directory", "Verified domain names."),
        new("Policy.Read.All", "Conditional Access", "Conditional Access policy coverage."),
        new("RoleManagement.Read.Directory", "Privileged access", "Directory roles, standing assignments and PIM eligibility."),
        new("SecurityEvents.Read.All", "Security", "Secure score and security alerts."),
        new("AuditLog.Read.All", "Audit", "Directory audit data used for evidence correlation."),
        new("Reports.Read.All", "Usage", "Workload usage reports used for licensing and Copilot readiness."),
        new("DeviceManagementManagedDevices.Read.All", "Endpoint DLP", "Device onboarding coverage."),
        new("Sites.Read.All", "Data map", "SharePoint site counts."),
        new("Team.ReadBasic.All", "Data map", "Team counts.")
    };

    public static IReadOnlyList<string> Names { get; } =
        Required.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>True when every permission is a read-only role; guards against a write scope creeping in.</summary>
    public static bool IsReadOnly(IEnumerable<string> names) =>
        names.All(n => n.Contains(".Read", StringComparison.OrdinalIgnoreCase) &&
                       !n.Contains("ReadWrite", StringComparison.OrdinalIgnoreCase));
}
