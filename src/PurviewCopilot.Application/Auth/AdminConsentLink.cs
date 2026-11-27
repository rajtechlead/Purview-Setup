using System.Globalization;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Auth;

/// <summary>
/// Builds the Microsoft identity admin-consent URL used when programmatic consent cannot be
/// granted. The link is tenant-specific and carries no secret, so it is safe to show and copy.
/// </summary>
public static class AdminConsentLink
{
    public static string Build(
        CloudEnvironment cloud,
        string tenantId,
        string clientId,
        string? redirectUri = null)
    {
        if (!Guid.TryParse(tenantId, out _))
            throw new InvalidOperationException("A valid tenant id is required to build the consent link.");
        if (!Guid.TryParse(clientId, out _))
            throw new InvalidOperationException("A valid application (client) id is required to build the consent link.");

        var authority = CloudProfiles.For(cloud).AuthorityHost;
        var endpoint = new Uri(authority, string.Create(
            CultureInfo.InvariantCulture,
            $"{Uri.EscapeDataString(tenantId)}/v2.0/adminconsent"));

        var query = $"client_id={Uri.EscapeDataString(clientId)}";
        if (!string.IsNullOrWhiteSpace(redirectUri))
        {
            query += $"&redirect_uri={Uri.EscapeDataString(redirectUri)}";
        }

        return $"{endpoint}?{query}";
    }
}
