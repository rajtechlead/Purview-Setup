using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Approvals;
using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Collaboration;

public sealed record CollaborationLimits(int MaxPayloadBytes = 1_048_576, int MaxOperations = 500, int MaxPageSize = 200);

/// <summary>
/// The shared, customer-hosted store. Every method takes a tenant id that the caller has already
/// been authorised for, and every query filters on it, so a client-supplied id cannot cross tenants.
/// </summary>
public sealed class CollaborationStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.General);

    private readonly IDbContextFactory<CollaborationDbContext> _factory;
    private readonly IClock _clock;
    private readonly CollaborationLimits _limits;

    public CollaborationStore(
        IDbContextFactory<CollaborationDbContext> factory,
        IClock clock,
        CollaborationLimits? limits = null)
    {
        _factory = factory;
        _clock = clock;
        _limits = limits ?? new CollaborationLimits();
    }

    /// <summary>
    /// Applies the versioned EF Core migrations on first use and records the store version. A database
    /// created by a pre-migration build must be recreated; no such build has shipped.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        if (!await db.Schema.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            db.Schema.Add(new CollaborationSchema
            {
                Id = 1,
                Version = 1,
                AppliedUtc = _clock.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // ---------------- memberships ----------------

    public async Task<CollaborationRole?> GetRoleAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        RequireTenant(tenantId);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var stored = await db.Memberships
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.UserId == userId)
            .Select(m => m.Role)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return RolePermissions.TryParse(stored, out var role) ? role : null;
    }

    /// <summary>Tenants the caller can see, used by the desktop client to pick a workspace.</summary>
    public async Task<IReadOnlyList<TenantMemberView>> ListMembershipsAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<TenantMemberView>();
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Memberships
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.TenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(m => new TenantMemberView(
                m.TenantId, m.UserId,
                RolePermissions.TryParse(m.Role, out var role) ? role : CollaborationRole.Viewer,
                m.CreatedUtc))
            .ToList();
    }

    public async Task<IReadOnlyList<TenantMemberView>> ListMembersAsync(
        string tenantId, CancellationToken cancellationToken = default)
    {
        RequireTenant(tenantId);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Memberships
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .OrderBy(m => m.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(m => new TenantMemberView(
                m.TenantId, m.UserId,
                RolePermissions.TryParse(m.Role, out var role) ? role : CollaborationRole.Viewer,
                m.CreatedUtc))
            .ToList();
    }

    public async Task<TenantMemberView> GrantMemberAsync(
        string tenantId, string userId, CollaborationRole role, string actor, CancellationToken cancellationToken = default)
    {
        RequireTenant(tenantId);

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new CollaborationValidationException("A member user id is required.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new CollaborationValidationException($"Unknown role '{role}'.");
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        var existing = await db.Memberships
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        var action = existing is null ? "MemberGranted" : "MemberRoleChanged";

        if (existing is null)
        {
            db.Memberships.Add(new TenantMembership
            {
                TenantId = tenantId,
                UserId = userId,
                Role = role.ToString(),
                CreatedUtc = _clock.UtcNow,
                CreatedBy = actor
            });
        }
        else
        {
            existing.Role = role.ToString();
        }

        await AppendAuditAsync(db, tenantId, actor, action, userId, $"Role={role}", cancellationToken).ConfigureAwait(false);
        await SaveAsync(db, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new TenantMemberView(tenantId, userId, role, existing?.CreatedUtc ?? _clock.UtcNow);
    }

    /// <summary>Removes a membership, refusing to leave a tenant without an Application Administrator.</summary>
    public async Task RemoveMemberAsync(
        string tenantId, string userId, string actor, CancellationToken cancellationToken = default)
    {
        RequireTenant(tenantId);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        var membership = await db.Memberships
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CollaborationNotFoundException($"'{userId}' is not a member of tenant '{tenantId}'.");

        var wasAdministrator = RolePermissions.TryParse(membership.Role, out var role) &&
                               role == CollaborationRole.ApplicationAdministrator;

        if (wasAdministrator)
        {
            var administrators = await db.Memberships
                .CountAsync(m => m.TenantId == tenantId && m.Role == nameof(CollaborationRole.ApplicationAdministrator), cancellationToken)
                .ConfigureAwait(false);

            if (administrators <= 1)
            {
                throw new CollaborationValidationException(
                    "The last Application Administrator cannot be removed; grant another administrator first.");
            }
        }

        db.Memberships.Remove(membership);
        await AppendAuditAsync(db, tenantId, actor, "MemberRemoved", userId, "Membership removed", cancellationToken)
            .ConfigureAwait(false);
        await SaveAsync(db, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---------------- assessments ----------------

    public async Task<IReadOnlyList<SharedAssessmentView>> ListAssessmentsAsync(
        string tenantId, int take = 50, CancellationToken cancellationToken = default)
    {
        RequireTenant(tenantId);
        take = Math.Clamp(take, 1, _limits.MaxPageSize);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Assessments
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.RunUtc)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(a => new SharedAssessmentView(
            a.Id, a.TenantId, a.RunUtc, a.Overall, a.Security, a.Governance, a.Compliance,
            a.Risk, a.Findings, a.CreatedBy, a.CreatedUtc)).ToList();
    }

    public async Task<SharedAssessmentView> PublishAssessmentAsync(
        string tenantId,
        AssessmentSubmission submission,
        string actor,
        CancellationToken cancellationToken = default)
    {
        RequireTenant(tenantId);
        ValidateAssessment(submission);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        var entity = new SharedAssessment
        {
            Id = submission.Id == Guid.Empty ? Guid.NewGuid() : submission.Id,
            TenantId = tenantId,
            RunUtc = submission.RunUtc == default ? _clock.UtcNow : submission.RunUtc,
            Overall = submission.Overall,
            Security = submission.Security,
            Governance = submission.Governance,
            Compliance = submission.Compliance,
            Risk = submission.Risk,
            Findings = submission.Findings,
            PayloadJson = submission.PayloadJson,
            CreatedBy = actor,
            CreatedUtc = _clock.UtcNow
        };

        db.Assessments.Add(entity);
        await AppendAuditAsync(db, tenantId, actor, "AssessmentPublished", entity.Id.ToString(),
            $"Overall={entity.Overall:0.#}; Findings={entity.Findings}", cancellationToken).ConfigureAwait(false);
        await SaveAsync(db, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new SharedAssessmentView(
            entity.Id, entity.TenantId, entity.RunUtc, entity.Overall, entity.Security, entity.Governance,
            entity.Compliance, entity.Risk, entity.Findings, entity.CreatedBy, entity.CreatedUtc);
    }

    // ---------------- plans ----------------

    public async Task<IReadOnlyList<SharedPlanView>> ListPlansAsync(
        string tenantId, int take = 50, CancellationToken cancellationToken = default)
    {
        RequireTenant(tenantId);
        take = Math.Clamp(take, 1, _limits.MaxPageSize);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Plans
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.CreatedUtc)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToPlanView).ToList();
    }

    public async Task<SharedPlanView> PublishPlanAsync(
        string tenantId, ChangePlan plan, string actor, CancellationToken cancellationToken = default)
    {
        RequireTenant(tenantId);

        if (plan is null)
        {
            throw new CollaborationValidationException("A change plan is required.");
        }

        if (plan.Operations.Count == 0)
        {
            throw new CollaborationValidationException("A change plan must contain at least one operation.");
        }

        if (plan.Operations.Count > _limits.MaxOperations)
        {
            throw new CollaborationValidationException($"A change plan cannot exceed {_limits.MaxOperations} operations.");
        }

        // The server owns identity, ownership and timing; a client cannot publish into another tenant.
        var stored = plan with
        {
            Id = plan.Id == Guid.Empty ? Guid.NewGuid() : plan.Id,
            TenantId = tenantId,
            CreatedBy = actor,
            CreatedUtc = _clock.UtcNow
        };

        var payload = JsonSerializer.Serialize(stored, Json);
        EnsurePayload(payload);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        var existing = await db.Plans.AnyAsync(p => p.Id == stored.Id, cancellationToken).ConfigureAwait(false);
        if (existing)
        {
            throw new CollaborationValidationException($"A plan with id '{stored.Id}' already exists.");
        }

        var entity = new SharedPlan
        {
            Id = stored.Id,
            TenantId = tenantId,
            Title = stored.Title,
            Mode = stored.Mode.ToString(),
            State = ApprovalState.Draft.ToString(),
            PlanHash = ChangePlanHasher.Compute(stored),
            PayloadJson = payload,
            CreatedBy = actor,
            CreatedUtc = _clock.UtcNow
        };

        db.Plans.Add(entity);
        await AppendAuditAsync(db, tenantId, actor, "PlanPublished", entity.Id.ToString(),
            $"Operations={stored.Operations.Count}; Destructive={stored.DestructiveOperationCount}", cancellationToken)
            .ConfigureAwait(false);
        await SaveAsync(db, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ToPlanView(entity);
    }

    // ---------------- approvals ----------------

    public async Task<SharedApprovalView> StartApprovalAsync(
        string tenantId, Guid planId, string actor, CancellationToken cancellationToken = default)
    {
        RequireTenant(tenantId);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        var planEntity = await LoadPlanEntityAsync(db, tenantId, planId, cancellationToken).ConfigureAwait(false);
        var plan = Deserialize<ChangePlan>(planEntity.PayloadJson);
        var existing = await db.Approvals
            .FirstOrDefaultAsync(a => a.PlanId == planId && a.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            var previous = Deserialize<ApprovalRequest>(existing.PayloadJson);
            if (previous.State is ApprovalState.Approved or ApprovalState.Applied)
            {
                throw new ApprovalRuleException(
                    $"The plan is already {previous.State}; a new approval round is not allowed.");
            }

            db.Approvals.Remove(existing);
        }

        var request = ApprovalWorkflow.Start(planId, _clock);
        request = ApprovalWorkflow.Submit(request, actor, _clock, ChangePlanHasher.Compute(plan));

        var entity = new SharedApproval
        {
            Id = request.Id,
            PlanId = planId,
            TenantId = tenantId,
            State = request.State.ToString(),
            PlanHash = request.PlanHash,
            ExpiresUtc = request.ExpiresUtc,
            PayloadJson = JsonSerializer.Serialize(request, Json),
            UpdatedUtc = request.UpdatedUtc
        };

        db.Approvals.Add(entity);
        await AppendAuditAsync(db, tenantId, actor, "ApprovalStarted", planId.ToString(),
            $"Stage={ApprovalWorkflow.CurrentStage(request)}; Expires={request.ExpiresUtc:u}", cancellationToken)
            .ConfigureAwait(false);
        await SaveAsync(db, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ToApprovalView(tenantId, request, entity.ConcurrencyStamp);
    }

    public async Task<SharedApprovalView> DecideApprovalAsync(
        string tenantId,
        Guid planId,
        ApprovalDecision decision,
        string actor,
        string comment,
        string? expectedStamp = null,
        CancellationToken cancellationToken = default)
    {
        RequireTenant(tenantId);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        var entity = await db.Approvals
            .FirstOrDefaultAsync(a => a.PlanId == planId && a.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CollaborationNotFoundException($"No approval exists for plan '{planId}' in tenant '{tenantId}'.");

        if (!string.IsNullOrWhiteSpace(expectedStamp) &&
            !string.Equals(expectedStamp, entity.ConcurrencyStamp.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ConcurrencyConflictException(
                "The approval was updated by someone else. Reload it and decide again.");
        }

        var planEntity = await LoadPlanEntityAsync(db, tenantId, planId, cancellationToken).ConfigureAwait(false);
        var plan = Deserialize<ChangePlan>(planEntity.PayloadJson);
        var request = Deserialize<ApprovalRequest>(entity.PayloadJson);

        ApprovalRequest updated;
        try
        {
            updated = ApprovalWorkflow.Decide(
                request, decision, actor, comment, _clock, ChangePlanHasher.Compute(plan));
        }
        catch (InvalidOperationException exception)
        {
            throw new ApprovalRuleException(exception.Message);
        }

        entity.State = updated.State.ToString();
        entity.PlanHash = updated.PlanHash;
        entity.ExpiresUtc = updated.ExpiresUtc;
        entity.PayloadJson = JsonSerializer.Serialize(updated, Json);
        entity.UpdatedUtc = updated.UpdatedUtc;

        planEntity.State = updated.State.ToString();

        await AppendAuditAsync(db, tenantId, actor, $"Approval{decision}", planId.ToString(),
            $"Stage={ApprovalWorkflow.CurrentStage(request)}; NewState={updated.State}; Comment={comment}", cancellationToken)
            .ConfigureAwait(false);
        await SaveAsync(db, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ToApprovalView(tenantId, updated, entity.ConcurrencyStamp);
    }

    public async Task<SharedApprovalView?> GetApprovalAsync(
        string tenantId, Guid planId, CancellationToken cancellationToken = default)
    {
        RequireTenant(tenantId);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.Approvals
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.PlanId == planId && a.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        return entity is null
            ? null
            : ToApprovalView(tenantId, Deserialize<ApprovalRequest>(entity.PayloadJson), entity.ConcurrencyStamp);
    }

    // ---------------- audit ----------------

    public async Task<IReadOnlyList<SharedAuditView>> ListAuditAsync(
        string tenantId, int take = 100, CancellationToken cancellationToken = default)
    {
        RequireTenant(tenantId);
        take = Math.Clamp(take, 1, _limits.MaxPageSize);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Audit
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.Sequence)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(a => new SharedAuditView(
            a.Sequence, a.AtUtc, a.Actor, a.Action, a.Subject, a.Detail, a.Hash)).ToList();
    }

    public async Task<AuditVerification> VerifyAuditAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        RequireTenant(tenantId);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Audit
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderBy(a => a.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var previous = AuditChain.Genesis;
        var expectedSequence = 1;

        foreach (var row in rows)
        {
            var computed = AuditChain.Compute(
                previous, row.TenantId, row.Sequence, row.AtUtc, row.Actor, row.Action, row.Subject, row.Detail);

            if (row.Sequence != expectedSequence || !string.Equals(computed, row.Hash, StringComparison.Ordinal) ||
                !string.Equals(previous, row.PrevHash, StringComparison.Ordinal))
            {
                return new AuditVerification(
                    false, expectedSequence - 1, row.Sequence,
                    $"Audit chain is broken at sequence {row.Sequence}.");
            }

            previous = row.Hash;
            expectedSequence++;
        }

        return new AuditVerification(true, rows.Count, null, $"Audit chain verified across {rows.Count} entries.");
    }

    // ---------------- helpers ----------------

    private async Task AppendAuditAsync(
        CollaborationDbContext db,
        string tenantId,
        string actor,
        string action,
        string subject,
        string detail,
        CancellationToken cancellationToken)
    {
        var last = await db.Audit
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.Sequence)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var sequence = (last?.Sequence ?? 0) + 1;
        var previous = last?.Hash ?? AuditChain.Genesis;
        var at = _clock.UtcNow;

        db.Audit.Add(new SharedAuditEntry
        {
            TenantId = tenantId,
            Sequence = sequence,
            AtUtc = at,
            Actor = actor,
            Action = action,
            Subject = subject,
            Detail = detail,
            PrevHash = previous,
            Hash = AuditChain.Compute(previous, tenantId, sequence, at, actor, action, subject, detail)
        });
    }

    private static async Task SaveAsync(CollaborationDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException(
                "Someone else changed this record first. Reload it and retry.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is not null)
        {
            throw new ConcurrencyConflictException(
                "The record could not be saved because another change won the race. Reload it and retry.");
        }
    }

    private async Task<SharedPlan> LoadPlanEntityAsync(
        CollaborationDbContext db, string tenantId, Guid planId, CancellationToken cancellationToken)
    {
        return await db.Plans
            .FirstOrDefaultAsync(p => p.Id == planId && p.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CollaborationNotFoundException($"Plan '{planId}' was not found in tenant '{tenantId}'.");
    }

    private void ValidateAssessment(AssessmentSubmission submission)
    {
        if (submission is null)
        {
            throw new CollaborationValidationException("An assessment payload is required.");
        }

        EnsureScore(submission.Overall, nameof(submission.Overall));
        EnsureScore(submission.Security, nameof(submission.Security));
        EnsureScore(submission.Governance, nameof(submission.Governance));
        EnsureScore(submission.Compliance, nameof(submission.Compliance));
        EnsureScore(submission.Risk, nameof(submission.Risk));

        if (submission.Findings < 0)
        {
            throw new CollaborationValidationException("The finding count cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(submission.PayloadJson))
        {
            throw new CollaborationValidationException("The assessment payload is empty.");
        }

        EnsurePayload(submission.PayloadJson);

        try
        {
            using var _ = JsonDocument.Parse(submission.PayloadJson);
        }
        catch (JsonException)
        {
            throw new CollaborationValidationException("The assessment payload is not valid JSON.");
        }
    }

    private static void EnsureScore(double value, string name)
    {
        if (double.IsNaN(value) || value < 0 || value > 100)
        {
            throw new CollaborationValidationException($"{name} must be between 0 and 100.");
        }
    }

    private void EnsurePayload(string payload)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(payload) > _limits.MaxPayloadBytes)
        {
            throw new CollaborationValidationException(
                $"The payload exceeds the {_limits.MaxPayloadBytes} byte limit.");
        }
    }

    private static void RequireTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 128)
        {
            throw new CollaborationValidationException("A valid tenant id is required.");
        }
    }

    private static T Deserialize<T>(string payload) where T : class =>
        JsonSerializer.Deserialize<T>(payload, Json)
        ?? throw new CollaborationValidationException("The stored record could not be read.");

    private static SharedPlanView ToPlanView(SharedPlan plan) =>
        new(plan.Id, plan.TenantId, plan.Title, plan.Mode, plan.State, plan.PlanHash,
            plan.CreatedBy, plan.CreatedUtc, CountOperations(plan.PayloadJson));

    private static int CountOperations(string payloadJson)
    {
        try
        {
            return Deserialize<ChangePlan>(payloadJson).Operations.Count;
        }
        catch (CollaborationException)
        {
            return 0;
        }
    }

    private static SharedApprovalView ToApprovalView(string tenantId, ApprovalRequest request, Guid stamp) =>
        new(request.Id, request.PlanId, tenantId, request.State.ToString(), request.PlanHash,
            request.ExpiresUtc, request.RequestedBy, request.History.Count, request.UpdatedUtc, stamp.ToString());
}

/// <summary>Client-supplied assessment summary. Scores are validated server-side.</summary>
public sealed record AssessmentSubmission(
    Guid Id,
    DateTimeOffset RunUtc,
    double Overall,
    double Security,
    double Governance,
    double Compliance,
    double Risk,
    int Findings,
    string PayloadJson);

/// <summary>Authorisation gate used by the service before it touches the store.</summary>
public sealed class TenantAuthorizationService
{
    private readonly CollaborationStore _store;

    public TenantAuthorizationService(CollaborationStore store) => _store = store;

    public async Task<CollaborationRole> RequireAsync(
        string tenantId,
        string userId,
        CollaborationPermission permission,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new TenantAccessDeniedException("<anonymous>", tenantId);
        }

        var role = await _store.GetRoleAsync(tenantId, userId, cancellationToken).ConfigureAwait(false)
            ?? throw new TenantAccessDeniedException(userId, tenantId);

        if (!RolePermissions.Allows(role, permission))
        {
            throw new InsufficientRoleException(role, permission);
        }

        return role;
    }
}
