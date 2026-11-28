using Microsoft.EntityFrameworkCore;

namespace PurviewCopilot.Collaboration;

public sealed record StoreCopyReport(
    int SchemaMarkers,
    int Memberships,
    int Assessments,
    int Plans,
    int Approvals,
    int AuditEntries)
{
    public int Total => SchemaMarkers + Memberships + Assessments + Plans + Approvals + AuditEntries;
}

/// <summary>
/// Copies a single-node SQLite collaboration store into another relational store (typically SQL
/// Server) so a team can move from one workstation to the central service. Identifiers are kept for
/// the GUID-keyed records, approval/plan links stay intact, audit sequence numbers are preserved,
/// and an identity column is left for the target to assign. The target must be empty: merging two
/// stores would silently drop memberships or fork an audit chain.
/// </summary>
public static class StoreCopier
{
    public static async Task<StoreCopyReport> CopyAsync(
        CollaborationDbContext source,
        CollaborationDbContext target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (ReferenceEquals(source, target))
            throw new InvalidOperationException("Source and target must be different stores.");

        var existing = await target.Memberships.CountAsync(cancellationToken).ConfigureAwait(false)
                       + await target.Assessments.CountAsync(cancellationToken).ConfigureAwait(false)
                       + await target.Plans.CountAsync(cancellationToken).ConfigureAwait(false)
                       + await target.Approvals.CountAsync(cancellationToken).ConfigureAwait(false)
                       + await target.Audit.CountAsync(cancellationToken).ConfigureAwait(false);

        if (existing > 0)
            throw new InvalidOperationException(
                $"The target store is not empty ({existing} row(s)). Copy into a fresh database; merging stores is not supported.");

        var schema = await source.Schema.AsNoTracking().OrderBy(s => s.Id).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var memberships = await source.Memberships.AsNoTracking().OrderBy(m => m.Id).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var assessments = await source.Assessments.AsNoTracking().OrderBy(a => a.RunUtc).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var plans = await source.Plans.AsNoTracking().OrderBy(p => p.CreatedUtc).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var approvals = await source.Approvals.AsNoTracking().OrderBy(a => a.UpdatedUtc).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var audit = await source.Audit.AsNoTracking().OrderBy(a => a.Sequence).ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var marker in schema)
        {
            target.Schema.Add(new CollaborationSchema
            {
                Id = 0,
                Version = marker.Version,
                AppliedUtc = marker.AppliedUtc
            });
        }

        foreach (var member in memberships)
        {
            target.Memberships.Add(new TenantMembership
            {
                Id = 0,
                TenantId = member.TenantId,
                UserId = member.UserId,
                Role = member.Role,
                CreatedUtc = member.CreatedUtc,
                CreatedBy = member.CreatedBy,
                ConcurrencyStamp = member.ConcurrencyStamp
            });
        }

        foreach (var assessment in assessments)
        {
            target.Assessments.Add(new SharedAssessment
            {
                Id = assessment.Id,
                TenantId = assessment.TenantId,
                RunUtc = assessment.RunUtc,
                Overall = assessment.Overall,
                Security = assessment.Security,
                Governance = assessment.Governance,
                Compliance = assessment.Compliance,
                Risk = assessment.Risk,
                Findings = assessment.Findings,
                PayloadJson = assessment.PayloadJson,
                CreatedBy = assessment.CreatedBy,
                CreatedUtc = assessment.CreatedUtc,
                ConcurrencyStamp = assessment.ConcurrencyStamp
            });
        }

        foreach (var plan in plans)
        {
            target.Plans.Add(new SharedPlan
            {
                Id = plan.Id,
                TenantId = plan.TenantId,
                Title = plan.Title,
                Mode = plan.Mode,
                State = plan.State,
                PlanHash = plan.PlanHash,
                PayloadJson = plan.PayloadJson,
                CreatedBy = plan.CreatedBy,
                CreatedUtc = plan.CreatedUtc,
                ConcurrencyStamp = plan.ConcurrencyStamp
            });
        }

        foreach (var approval in approvals)
        {
            target.Approvals.Add(new SharedApproval
            {
                Id = approval.Id,
                PlanId = approval.PlanId,
                TenantId = approval.TenantId,
                State = approval.State,
                PlanHash = approval.PlanHash,
                ExpiresUtc = approval.ExpiresUtc,
                PayloadJson = approval.PayloadJson,
                UpdatedUtc = approval.UpdatedUtc,
                ConcurrencyStamp = approval.ConcurrencyStamp
            });
        }

        foreach (var entry in audit)
        {
            target.Audit.Add(new SharedAuditEntry
            {
                Id = 0,
                TenantId = entry.TenantId,
                Sequence = entry.Sequence,
                AtUtc = entry.AtUtc,
                Actor = entry.Actor,
                Action = entry.Action,
                Subject = entry.Subject,
                Detail = entry.Detail,
                PrevHash = entry.PrevHash,
                Hash = entry.Hash
            });
        }

        await target.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new StoreCopyReport(
            schema.Count, memberships.Count, assessments.Count, plans.Count, approvals.Count, audit.Count);
    }
}
