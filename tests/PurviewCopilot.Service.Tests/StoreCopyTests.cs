using Microsoft.EntityFrameworkCore;
using PurviewCopilot.Collaboration;
using Xunit;

namespace PurviewCopilot.Service.Tests;

public class StoreCopyTests : IAsyncLifetime
{
    private readonly string _sourcePath = Path.Combine(Path.GetTempPath(), $"pgc-copy-src-{Guid.NewGuid():N}.db");
    private readonly string _targetPath = Path.Combine(Path.GetTempPath(), $"pgc-copy-dst-{Guid.NewGuid():N}.db");

    private static CollaborationDbContext Create(string path) =>
        new(CollaborationStoreFactory.FromConfiguration("Sqlite", $"Data Source={path}"));

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var path in new[] { _sourcePath, _targetPath })
        {
            if (File.Exists(path)) File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private async Task SeedAsync()
    {
        await using var source = Create(_sourcePath);
        await source.Database.MigrateAsync();

        source.Memberships.Add(new TenantMembership
        {
            TenantId = "contoso", UserId = "alice", Role = nameof(CollaborationRole.Approver),
            CreatedUtc = DateTimeOffset.UtcNow, CreatedBy = "admin", ConcurrencyStamp = Guid.NewGuid()
        });

        var assessmentId = Guid.NewGuid();
        source.Assessments.Add(new SharedAssessment
        {
            Id = assessmentId, TenantId = "contoso", RunUtc = DateTimeOffset.UtcNow, Overall = 71,
            Security = 70, Governance = 72, Compliance = 73, Risk = 69, Findings = 12,
            PayloadJson = "{}", CreatedBy = "alice", CreatedUtc = DateTimeOffset.UtcNow,
            ConcurrencyStamp = Guid.NewGuid()
        });

        var planId = Guid.NewGuid();
        source.Plans.Add(new SharedPlan
        {
            Id = planId, TenantId = "contoso", Title = "Plan", Mode = "GuidedDeployment", State = "PendingCompliance",
            PlanHash = "hash", PayloadJson = "{}", CreatedBy = "alice", CreatedUtc = DateTimeOffset.UtcNow,
            ConcurrencyStamp = Guid.NewGuid()
        });

        source.Approvals.Add(new SharedApproval
        {
            Id = Guid.NewGuid(), PlanId = planId, TenantId = "contoso", State = "PendingSecurity",
            PlanHash = "hash", PayloadJson = "{}", UpdatedUtc = DateTimeOffset.UtcNow, ConcurrencyStamp = Guid.NewGuid()
        });

        source.Audit.Add(new SharedAuditEntry
        {
            TenantId = "contoso", Sequence = 1, AtUtc = DateTimeOffset.UtcNow, Actor = "alice",
            Action = "AssessmentPublished", Subject = assessmentId.ToString(), Detail = "", PrevHash = "genesis", Hash = "h1"
        });

        await source.SaveChangesAsync();
    }

    [Fact]
    public async Task CopyMovesEveryTableAndKeepsLinks()
    {
        await SeedAsync();

        int planCount;
        Guid copiedPlanId;
        Guid copiedApprovalPlanId;
        int auditSequence;

        await using (var source = Create(_sourcePath))
        await using (var target = Create(_targetPath))
        {
            await target.Database.MigrateAsync();
            var report = await StoreCopier.CopyAsync(source, target);

            Assert.Equal(1, report.Memberships);
            Assert.Equal(1, report.Assessments);
            Assert.Equal(1, report.Plans);
            Assert.Equal(1, report.Approvals);
            Assert.Equal(1, report.AuditEntries);
            Assert.Equal(5, report.Total);
        }

        await using (var target = Create(_targetPath))
        {
            planCount = await target.Plans.CountAsync();
            copiedPlanId = (await target.Plans.SingleAsync()).Id;
            copiedApprovalPlanId = (await target.Approvals.SingleAsync()).PlanId;
            auditSequence = (await target.Audit.SingleAsync()).Sequence;
            Assert.Equal("contoso", (await target.Memberships.SingleAsync()).TenantId);
        }

        Assert.Equal(1, planCount);
        Assert.Equal(copiedPlanId, copiedApprovalPlanId);
        Assert.Equal(1, auditSequence);
    }

    [Fact]
    public async Task CopyRefusesANonEmptyTarget()
    {
        await SeedAsync();

        await using var source = Create(_sourcePath);
        await using var target = Create(_targetPath);
        await target.Database.MigrateAsync();
        await StoreCopier.CopyAsync(source, target);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => StoreCopier.CopyAsync(source, target));
        Assert.Contains("not empty", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopyRefusesTheSameStore()
    {
        await SeedAsync();
        await using var source = Create(_sourcePath);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => StoreCopier.CopyAsync(source, source));
        Assert.Contains("different stores", error.Message, StringComparison.Ordinal);
    }
}
