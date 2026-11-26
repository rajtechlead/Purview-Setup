using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Workspace;

/// <summary>
/// Customer-level permissions for the workstation. Access is explicit per tenant; a profile with
/// no assignments is a legacy profile and remains available, while new saves always record the
/// creator as Administrator.
/// </summary>
public static class TenantAccessPolicy
{
    public static IReadOnlyList<TenantAccess> Normalize(IEnumerable<TenantAccess>? access, string owner)
    {
        var assignments = (access ?? Array.Empty<TenantAccess>())
            .Where(a => !string.IsNullOrWhiteSpace(a.Identity))
            .GroupBy(a => a.Identity.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new TenantAccess(g.Key, g.Last().Role))
            .ToList();

        var ownerName = string.IsNullOrWhiteSpace(owner) ? Environment.UserName : owner.Trim();
        var existingOwner = assignments.FindIndex(a =>
            string.Equals(a.Identity, ownerName, StringComparison.OrdinalIgnoreCase));

        if (existingOwner >= 0)
        {
            if (assignments[existingOwner].Role != UserRole.Administrator)
                assignments[existingOwner] = new TenantAccess(ownerName, UserRole.Administrator);
        }
        else
        {
            assignments.Insert(0, new TenantAccess(ownerName, UserRole.Administrator));
        }

        if (!assignments.Any(a => a.Role == UserRole.Administrator))
            assignments.Insert(0, new TenantAccess(ownerName, UserRole.Administrator));

        return assignments;
    }

    public static UserRole? RoleFor(TenantProfile profile, string identity)
    {
        if (profile.Access is null || profile.Access.Count == 0)
        {
            // Legacy profile: only the recorded owner is known; owner-less profiles stay usable.
            return string.IsNullOrWhiteSpace(profile.Owner) ||
                   string.Equals(profile.Owner, identity, StringComparison.OrdinalIgnoreCase)
                ? UserRole.Administrator
                : null;
        }

        return profile.Access
            .FirstOrDefault(a => string.Equals(a.Identity, identity, StringComparison.OrdinalIgnoreCase))
            ?.Role;
    }

    public static bool CanAccess(TenantProfile profile, string identity) => RoleFor(profile, identity) is not null;

    public static bool CanOperate(TenantProfile profile, string identity) =>
        RoleFor(profile, identity) is UserRole.Operator or UserRole.Approver or UserRole.Administrator;

    public static bool CanApprove(TenantProfile profile, string identity) =>
        RoleFor(profile, identity) is UserRole.Approver or UserRole.Administrator;

    public static bool CanManage(TenantProfile profile, string identity) =>
        RoleFor(profile, identity) == UserRole.Administrator;

    public static IReadOnlyList<TenantAccess> ParseAssignments(string text, string owner)
    {
        var assignments = new List<TenantAccess>();

        foreach (var entry in text.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
                throw new InvalidOperationException($"Access entry '{entry}' must be user=Role.");

            if (!Enum.TryParse<UserRole>(parts[1], ignoreCase: true, out var role))
                throw new InvalidOperationException(
                    $"Unknown role '{parts[1]}'. Use Viewer, Operator, Approver or Administrator.");

            assignments.Add(new TenantAccess(parts[0], role));
        }

        return Normalize(assignments, owner);
    }

    public static string Format(IEnumerable<TenantAccess>? access) =>
        string.Join("; ", (access ?? Array.Empty<TenantAccess>()).Select(a => $"{a.Identity}={a.Role}"));
}
