using System.Security.Claims;

namespace PurviewCopilot.Service;

/// <summary>
/// Resolves the caller from validated token claims. The object id is preferred because a UPN can
/// change; the user principal name is the fallback for tokens that omit it.
/// </summary>
public static class ServiceIdentity
{
    public static string? CallerId(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var objectId = principal.FindFirstValue("oid")
                       ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");

        if (!string.IsNullOrWhiteSpace(objectId))
        {
            return objectId;
        }

        var upn = principal.FindFirstValue("preferred_username")
                  ?? principal.FindFirstValue(ClaimTypes.Upn)
                  ?? principal.Identity.Name;

        return string.IsNullOrWhiteSpace(upn) ? null : upn;
    }

    public static IReadOnlyList<string> Roles(ClaimsPrincipal? principal) =>
        principal?.FindAll("roles").Select(c => c.Value).ToList()
        ?? new List<string>();
}
