using System.Text.Json;

namespace PurviewCopilot.Infrastructure.Auth;

public sealed record GraphAppRole(string Name, string Id);

/// <summary>
/// Resolves permission names to the app role ids published by the tenant's Microsoft Graph
/// service principal. Resolving at runtime keeps the product free of hardcoded role GUIDs and
/// fails loudly when a permission does not exist instead of requesting an unknown id.
/// </summary>
public static class GraphAppRoleCatalog
{
    public static (IReadOnlyList<GraphAppRole> Resolved, IReadOnlyList<string> Missing) Resolve(
        JsonElement servicePrincipal,
        IEnumerable<string> permissionNames)
    {
        var available = new Dictionary<string, GraphAppRole>(StringComparer.OrdinalIgnoreCase);

        if (servicePrincipal.ValueKind == JsonValueKind.Object &&
            servicePrincipal.TryGetProperty("appRoles", out var appRoles) &&
            appRoles.ValueKind == JsonValueKind.Array)
        {
            foreach (var role in appRoles.EnumerateArray())
            {
                if (role.ValueKind != JsonValueKind.Object) continue;

                var value = role.TryGetProperty("value", out var v) ? v.GetString() : null;
                var id = role.TryGetProperty("id", out var i) ? i.GetString() : null;
                if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(id)) continue;

                available[value] = new GraphAppRole(value, id);
            }
        }

        var resolved = new List<GraphAppRole>();
        var missing = new List<string>();

        foreach (var name in permissionNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (available.TryGetValue(name, out var role)) resolved.Add(role);
            else missing.Add(name);
        }

        return (resolved, missing);
    }
}
