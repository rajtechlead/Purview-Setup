namespace PurviewCopilot.Collaboration;

/// <summary>Base class for failures the service maps to a specific HTTP status.</summary>
public abstract class CollaborationException : Exception
{
    protected CollaborationException(string message) : base(message) { }
}

/// <summary>The caller has no membership on the requested tenant. Mapped to 403, never 404 leakage.</summary>
public sealed class TenantAccessDeniedException : CollaborationException
{
    public TenantAccessDeniedException(string userId, string tenantId)
        : base($"'{userId}' has no access to tenant '{tenantId}'.") { }
}

/// <summary>The caller is a member but the role does not grant the operation. Mapped to 403.</summary>
public sealed class InsufficientRoleException : CollaborationException
{
    public InsufficientRoleException(CollaborationRole role, CollaborationPermission permission)
        : base($"Role '{role}' does not grant '{permission}'.") { }
}

/// <summary>A supplied concurrency stamp is stale. Mapped to 409 so the client reloads and retries.</summary>
public sealed class ConcurrencyConflictException : CollaborationException
{
    public ConcurrencyConflictException(string message) : base(message) { }
}

/// <summary>A domain rule refused the request (expired approval, changed plan, self-approval). Mapped to 409.</summary>
public sealed class ApprovalRuleException : CollaborationException
{
    public ApprovalRuleException(string message) : base(message) { }
}

/// <summary>Submitted content failed validation. Mapped to 400.</summary>
public sealed class CollaborationValidationException : CollaborationException
{
    public CollaborationValidationException(string message) : base(message) { }
}

/// <summary>The tenant has no local collaboration data yet. Mapped to 404.</summary>
public sealed class CollaborationNotFoundException : CollaborationException
{
    public CollaborationNotFoundException(string message) : base(message) { }
}
