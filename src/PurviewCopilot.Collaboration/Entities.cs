namespace PurviewCopilot.Collaboration;

/// <summary>A user's role on one tenant. Users may hold different roles on different tenants.</summary>
public sealed class TenantMembership
{
    public long Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Entra object id (preferred) or user principal name of the member.</summary>
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = nameof(CollaborationRole.Viewer);
    public DateTimeOffset CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public Guid ConcurrencyStamp { get; set; }
}

/// <summary>An assessment published by one workstation and shared with the team.</summary>
public sealed class SharedAssessment
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public DateTimeOffset RunUtc { get; set; }
    public double Overall { get; set; }
    public double Security { get; set; }
    public double Governance { get; set; }
    public double Compliance { get; set; }
    public double Risk { get; set; }
    public int Findings { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public Guid ConcurrencyStamp { get; set; }
}

/// <summary>A change plan under review. The hash is computed by the server from the stored content.</summary>
public sealed class SharedPlan
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PlanHash { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public Guid ConcurrencyStamp { get; set; }
}

/// <summary>The durable approval request for a plan, stored as the domain record's JSON form.</summary>
public sealed class SharedApproval
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PlanHash { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresUtc { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; }
    public Guid ConcurrencyStamp { get; set; }
}

/// <summary>One link in the per-tenant audit chain.</summary>
public sealed class SharedAuditEntry
{
    public long Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public DateTimeOffset AtUtc { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string PrevHash { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
}

/// <summary>Schema marker so a future migration can detect the store version.</summary>
public sealed class CollaborationSchema
{
    public int Id { get; set; }
    public int Version { get; set; }
    public DateTimeOffset AppliedUtc { get; set; }
}

public sealed record TenantMemberView(string TenantId, string UserId, CollaborationRole Role, DateTimeOffset CreatedUtc);

public sealed record SharedAssessmentView(
    Guid Id, string TenantId, DateTimeOffset RunUtc, double Overall, double Security,
    double Governance, double Compliance, double Risk, int Findings, string CreatedBy, DateTimeOffset CreatedUtc);

public sealed record SharedPlanView(
    Guid Id, string TenantId, string Title, string Mode, string State, string PlanHash,
    string CreatedBy, DateTimeOffset CreatedUtc, int OperationCount);

public sealed record SharedApprovalView(
    Guid Id, Guid PlanId, string TenantId, string State, string PlanHash,
    DateTimeOffset? ExpiresUtc, string RequestedBy, int HistoryCount, DateTimeOffset UpdatedUtc, string ConcurrencyStamp);

public sealed record SharedAuditView(
    int Sequence, DateTimeOffset AtUtc, string Actor, string Action, string Subject, string Detail, string Hash);

public sealed record AuditVerification(bool Valid, int Checked, int? FirstBrokenSequence, string Message);
