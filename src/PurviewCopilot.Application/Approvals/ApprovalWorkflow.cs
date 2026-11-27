using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Approvals;

/// <summary>
/// Pure state machine for change approval: Draft → Security → Compliance → CAB → Approved.
/// Any rejection ends the request; a change request returns it to Draft for rework.
/// Keeping transitions pure makes them fully unit-testable and auditable.
/// </summary>
public static class ApprovalWorkflow
{
    /// <summary>How long a submitted approval stays valid. A stale approval must be re-submitted.</summary>
    public static readonly TimeSpan DefaultApprovalLifetime = TimeSpan.FromDays(7);

    private static readonly ApprovalStage[] Stages =
    {
        ApprovalStage.Security,
        ApprovalStage.Compliance,
        ApprovalStage.Cab
    };

    public static ApprovalRequest Start(Guid planId, IClock clock) =>
        new(Guid.NewGuid(), planId, ApprovalState.Draft, 0, Array.Empty<ApprovalEvent>(), clock.UtcNow, clock.UtcNow);

    public static ApprovalRequest Submit(
        ApprovalRequest request,
        string actor,
        IClock clock,
        string? planHash = null,
        TimeSpan? lifetime = null)
    {
        if (request.State != ApprovalState.Draft)
        {
            throw new InvalidOperationException($"Only a draft request can be submitted (current state: {request.State}).");
        }

        var requester = string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim();

        return request with
        {
            State = ApprovalState.PendingSecurity,
            CurrentStageIndex = 0,
            PlanHash = planHash ?? request.PlanHash,
            RequestedBy = requester,
            ExpiresUtc = clock.UtcNow + (lifetime ?? DefaultApprovalLifetime),
            UpdatedUtc = clock.UtcNow
        };
    }

    public static ApprovalRequest Decide(
        ApprovalRequest request,
        ApprovalDecision decision,
        string actor,
        string comment,
        IClock clock,
        string? currentPlanHash = null)
    {
        if (request.State is ApprovalState.Approved or ApprovalState.Rejected or ApprovalState.RolledBack)
        {
            throw new InvalidOperationException($"Request is already final ({request.State}).");
        }

        if (request.State is ApprovalState.Draft or ApprovalState.Applied)
        {
            throw new InvalidOperationException($"Request cannot be decided in state {request.State}.");
        }

        if (request.ExpiresUtc is { } expires && clock.UtcNow > expires)
        {
            throw new InvalidOperationException(
                $"The approval request expired at {expires:u}. Submit the plan again for a fresh approval.");
        }

        var actorName = string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim();

        if (!string.IsNullOrEmpty(currentPlanHash) &&
            !string.IsNullOrEmpty(request.PlanHash) &&
            !string.Equals(request.PlanHash, currentPlanHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The change plan changed after approval started, so the earlier decisions were invalidated. Submit the plan again.");
        }

        if (decision == ApprovalDecision.Approve &&
            !string.IsNullOrEmpty(request.RequestedBy) &&
            string.Equals(actorName, request.RequestedBy, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Self-approval is not allowed: the person who requested the change cannot approve it.");
        }

        var stage = Stages[Math.Clamp(request.CurrentStageIndex, 0, Stages.Length - 1)];

        var approvalEvent = new ApprovalEvent(
            Guid.NewGuid(), request.PlanId, stage, decision,
            actorName,
            comment ?? string.Empty, clock.UtcNow, request.PlanHash);

        var history = request.History.Concat(new[] { approvalEvent }).ToList();

        return decision switch
        {
            ApprovalDecision.Reject => request with
            {
                State = ApprovalState.Rejected,
                History = history,
                UpdatedUtc = clock.UtcNow
            },

            ApprovalDecision.RequestChanges => request with
            {
                State = ApprovalState.Draft,
                CurrentStageIndex = 0,
                History = history,
                UpdatedUtc = clock.UtcNow
            },

            _ => Advance(request, history, clock)
        };
    }

    public static ApprovalRequest MarkApplied(ApprovalRequest request, IClock clock)
    {
        if (request.State != ApprovalState.Approved)
        {
            throw new InvalidOperationException("Only an approved request can be applied.");
        }

        return request with { State = ApprovalState.Applied, UpdatedUtc = clock.UtcNow };
    }

    public static ApprovalRequest MarkRolledBack(ApprovalRequest request, IClock clock) =>
        request with { State = ApprovalState.RolledBack, UpdatedUtc = clock.UtcNow };

    public static ApprovalStage CurrentStage(ApprovalRequest request) =>
        Stages[Math.Clamp(request.CurrentStageIndex, 0, Stages.Length - 1)];

    public static bool RequiresCab(Guid planId, int destructiveOperations) => planId != Guid.Empty && destructiveOperations > 0;

    private static ApprovalRequest Advance(ApprovalRequest request, List<ApprovalEvent> history, IClock clock)
    {
        var nextIndex = request.CurrentStageIndex + 1;

        return nextIndex >= Stages.Length
            ? request with { State = ApprovalState.Approved, History = history, UpdatedUtc = clock.UtcNow }
            : request with
            {
                State = nextIndex switch
                {
                    1 => ApprovalState.PendingCompliance,
                    _ => ApprovalState.PendingCab
                },
                CurrentStageIndex = nextIndex,
                History = history,
                UpdatedUtc = clock.UtcNow
            };
    }

    public static IReadOnlyList<ApprovalStage> StageOrder => Stages;
}
