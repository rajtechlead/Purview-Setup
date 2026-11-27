namespace PurviewCopilot.Collaboration;

/// <summary>
/// The seven application roles. They are deliberately independent: no role inherits another,
/// so a permission is granted only where it is listed.
/// </summary>
public enum CollaborationRole
{
    Viewer = 0,
    Analyst,
    Designer,
    Operator,
    Approver,
    Auditor,
    ApplicationAdministrator
}

[Flags]
public enum CollaborationPermission
{
    None = 0,
    Read = 1,
    CreateAssessment = 2,
    CreatePlan = 4,
    StartApproval = 8,
    DecideApproval = 16,
    ReadAudit = 32,
    ManageMembers = 64
}

/// <summary>
/// Server-side permission matrix. Authorisation is always evaluated here, never by the client and
/// never by hiding a control in the desktop UI.
/// </summary>
public static class RolePermissions
{
    private static readonly IReadOnlyDictionary<CollaborationRole, CollaborationPermission> Matrix =
        new Dictionary<CollaborationRole, CollaborationPermission>
        {
            [CollaborationRole.Viewer] = CollaborationPermission.Read,

            [CollaborationRole.Analyst] = CollaborationPermission.Read | CollaborationPermission.CreateAssessment,

            [CollaborationRole.Designer] = CollaborationPermission.Read | CollaborationPermission.CreatePlan,

            [CollaborationRole.Operator] = CollaborationPermission.Read | CollaborationPermission.StartApproval,

            [CollaborationRole.Approver] = CollaborationPermission.Read | CollaborationPermission.DecideApproval,

            [CollaborationRole.Auditor] = CollaborationPermission.Read | CollaborationPermission.ReadAudit,

            [CollaborationRole.ApplicationAdministrator] =
                CollaborationPermission.Read |
                CollaborationPermission.CreateAssessment |
                CollaborationPermission.CreatePlan |
                CollaborationPermission.StartApproval |
                CollaborationPermission.DecideApproval |
                CollaborationPermission.ReadAudit |
                CollaborationPermission.ManageMembers
        };

    public static CollaborationPermission For(CollaborationRole role) =>
        Matrix.TryGetValue(role, out var permissions) ? permissions : CollaborationPermission.None;

    public static bool Allows(CollaborationRole role, CollaborationPermission permission) =>
        permission != CollaborationPermission.None && For(role).HasFlag(permission);

    public static bool TryParse(string? value, out CollaborationRole role) =>
        Enum.TryParse(value, ignoreCase: true, out role) && Enum.IsDefined(role);

    public static IReadOnlyList<CollaborationRole> All { get; } = Enum.GetValues<CollaborationRole>();
}
