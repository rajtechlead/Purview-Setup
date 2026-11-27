using Microsoft.EntityFrameworkCore;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Collaboration;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Service.Tests;

public class CollaborationStoreTests : IDisposable
{
    private readonly string _databasePath;
    private readonly DbContextOptions<CollaborationDbContext> _options;
    private readonly TestFactory _factory;
    private readonly MutableClock _clock = new();
    private readonly CollaborationStore _store;

    private const string TenantA = "contoso.onmicrosoft.com";
    private const string TenantB = "fabrikam.onmicrosoft.com";

    public CollaborationStoreTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"pgc-collab-{Guid.NewGuid():N}.db");
        _options = CollaborationStoreFactory.FromConfiguration("Sqlite", $"Data Source={_databasePath}");
        _factory = new TestFactory(_options);
        _store = new CollaborationStore(_factory, _clock);

        _store.InitializeAsync().GetAwaiter().GetResult();
        _store.GrantMemberAsync(TenantA, "alice", CollaborationRole.ApplicationAdministrator, "system").GetAwaiter().GetResult();
        _store.GrantMemberAsync(TenantA, "analyst", CollaborationRole.Analyst, "system").GetAwaiter().GetResult();
        _store.GrantMemberAsync(TenantA, "designer", CollaborationRole.Designer, "system").GetAwaiter().GetResult();
        _store.GrantMemberAsync(TenantA, "operator", CollaborationRole.Operator, "system").GetAwaiter().GetResult();
        _store.GrantMemberAsync(TenantA, "approver", CollaborationRole.Approver, "system").GetAwaiter().GetResult();
        _store.GrantMemberAsync(TenantA, "auditor", CollaborationRole.Auditor, "system").GetAwaiter().GetResult();
        _store.GrantMemberAsync(TenantA, "viewer", CollaborationRole.Viewer, "system").GetAwaiter().GetResult();
        _store.GrantMemberAsync(TenantB, "bob", CollaborationRole.Viewer, "system").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // A pooled SQLite connection may still hold the file; the temp directory is disposable.
        }
    }

    [Fact]
    public void RoleMatrix_IsLeastPrivilegeAndDoesNotInherit()
    {
        Assert.True(RolePermissions.Allows(CollaborationRole.Viewer, CollaborationPermission.Read));
        Assert.False(RolePermissions.Allows(CollaborationRole.Viewer, CollaborationPermission.CreateAssessment));
        Assert.False(RolePermissions.Allows(CollaborationRole.Analyst, CollaborationPermission.CreatePlan));
        Assert.False(RolePermissions.Allows(CollaborationRole.Designer, CollaborationPermission.StartApproval));
        Assert.False(RolePermissions.Allows(CollaborationRole.Operator, CollaborationPermission.DecideApproval));
        Assert.False(RolePermissions.Allows(CollaborationRole.Approver, CollaborationPermission.ManageMembers));
        Assert.False(RolePermissions.Allows(CollaborationRole.Auditor, CollaborationPermission.CreatePlan));
        Assert.True(RolePermissions.Allows(CollaborationRole.ApplicationAdministrator, CollaborationPermission.ManageMembers));
        Assert.Equal(7, RolePermissions.All.Count);
    }

    [Fact]
    public async Task TenantIsolation_DeniesAMemberOfAnotherTenant()
    {
        var authorization = new TenantAuthorizationService(_store);

        await Assert.ThrowsAsync<TenantAccessDeniedException>(() =>
            authorization.RequireAsync(TenantA, "bob", CollaborationPermission.Read));

        var role = await authorization.RequireAsync(TenantB, "bob", CollaborationPermission.Read);
        Assert.Equal(CollaborationRole.Viewer, role);
    }

    [Fact]
    public async Task TenantIsolation_FiltersEveryReadByTenant()
    {
        await _store.PublishAssessmentAsync(TenantA, Submission("alice"), "alice");

        Assert.Single(await _store.ListAssessmentsAsync(TenantA));
        Assert.Empty(await _store.ListAssessmentsAsync(TenantB));
    }

    [Fact]
    public async Task WrongRole_IsRefusedForTheOperation()
    {
        var authorization = new TenantAuthorizationService(_store);

        await Assert.ThrowsAsync<InsufficientRoleException>(() =>
            authorization.RequireAsync(TenantA, "viewer", CollaborationPermission.CreateAssessment));

        await Assert.ThrowsAsync<InsufficientRoleException>(() =>
            authorization.RequireAsync(TenantA, "approver", CollaborationPermission.ManageMembers));

        var analyst = await authorization.RequireAsync(TenantA, "analyst", CollaborationPermission.CreateAssessment);
        Assert.Equal(CollaborationRole.Analyst, analyst);
    }

    [Fact]
    public async Task Approval_IsDurableAndSurvivesANewStoreInstance()
    {
        var plan = await _store.PublishPlanAsync(TenantA, BuildPlan(), "designer");
        var started = await _store.StartApprovalAsync(TenantA, plan.Id, "operator");

        Assert.Equal("PendingSecurity", started.State);
        Assert.Equal("operator", started.RequestedBy);

        var restarted = new CollaborationStore(_factory, _clock);
        var reloaded = await restarted.GetApprovalAsync(TenantA, plan.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(started.PlanHash, reloaded!.PlanHash);

        var approval = await restarted.DecideApprovalAsync(TenantA, plan.Id, ApprovalDecision.Approve, "approver", "looks good");
        Assert.Equal("PendingCompliance", approval.State);

        approval = await restarted.DecideApprovalAsync(TenantA, plan.Id, ApprovalDecision.Approve, "approver", "ok");
        approval = await restarted.DecideApprovalAsync(TenantA, plan.Id, ApprovalDecision.Approve, "approver", "ok");

        Assert.Equal("Approved", approval.State);
        Assert.Equal(3, approval.HistoryCount);
    }

    [Fact]
    public async Task Approval_RefusesTheRequester()
    {
        var plan = await _store.PublishPlanAsync(TenantA, BuildPlan(), "designer");
        await _store.StartApprovalAsync(TenantA, plan.Id, "operator");

        var error = await Assert.ThrowsAsync<ApprovalRuleException>(() =>
            _store.DecideApprovalAsync(TenantA, plan.Id, ApprovalDecision.Approve, "operator", "self"));

        Assert.Contains("Self-approval", error.Message);
    }

    [Fact]
    public async Task Approval_Expires()
    {
        var plan = await _store.PublishPlanAsync(TenantA, BuildPlan(), "designer");
        await _store.StartApprovalAsync(TenantA, plan.Id, "operator");

        _clock.UtcNow = _clock.UtcNow.AddDays(8);

        var error = await Assert.ThrowsAsync<ApprovalRuleException>(() =>
            _store.DecideApprovalAsync(TenantA, plan.Id, ApprovalDecision.Approve, "approver", "late"));

        Assert.Contains("expired", error.Message);
    }

    [Fact]
    public async Task Approval_IsInvalidatedWhenTheStoredPlanIsEdited()
    {
        var plan = await _store.PublishPlanAsync(TenantA, BuildPlan(), "designer");
        await _store.StartApprovalAsync(TenantA, plan.Id, "operator");

        await using (var db = _factory.CreateDbContext())
        {
            var entity = await db.Plans.SingleAsync(p => p.Id == plan.Id);
            entity.PayloadJson = entity.PayloadJson.Replace("baseline", "tampered", StringComparison.Ordinal);
            await db.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<ApprovalRuleException>(() =>
            _store.DecideApprovalAsync(TenantA, plan.Id, ApprovalDecision.Approve, "approver", "ok"));

        Assert.Contains("changed", error.Message);
    }

    [Fact]
    public async Task Approval_RejectsAStaleConcurrencyStamp()
    {
        var plan = await _store.PublishPlanAsync(TenantA, BuildPlan(), "designer");
        await _store.StartApprovalAsync(TenantA, plan.Id, "operator");

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            _store.DecideApprovalAsync(TenantA, plan.Id, ApprovalDecision.Approve, "approver", "ok",
                expectedStamp: Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task Audit_IsChainedAndDetectsTampering()
    {
        await _store.PublishAssessmentAsync(TenantA, Submission("analyst"), "analyst");
        await _store.GrantMemberAsync(TenantB, "carol", CollaborationRole.Viewer, "system");

        var verified = await _store.VerifyAuditAsync(TenantA);
        Assert.True(verified.Valid);
        Assert.True(verified.Checked >= 8);

        await using (var db = _factory.CreateDbContext())
        {
            var entry = await db.Audit.Where(a => a.TenantId == TenantA).OrderBy(a => a.Sequence).Skip(2).FirstAsync();
            entry.Detail = "edited after the fact";
            await db.SaveChangesAsync();
        }

        var broken = await _store.VerifyAuditAsync(TenantA);
        Assert.False(broken.Valid);
        Assert.Equal(3, broken.FirstBrokenSequence);
    }

    [Fact]
    public async Task Audit_IsIsolatedPerTenant()
    {
        await _store.PublishAssessmentAsync(TenantA, Submission("analyst"), "analyst");

        var tenantB = await _store.ListAuditAsync(TenantB);
        Assert.All(tenantB, entry => Assert.DoesNotContain("AssessmentPublished", entry.Action));
        Assert.True((await _store.VerifyAuditAsync(TenantB)).Valid);
    }

    [Fact]
    public async Task Membership_RefusesToRemoveTheLastAdministrator()
    {
        var error = await Assert.ThrowsAsync<CollaborationValidationException>(() =>
            _store.RemoveMemberAsync(TenantA, "alice", "alice"));

        Assert.Contains("last Application Administrator", error.Message);
    }

    [Fact]
    public async Task Membership_RejectsUnknownRole()
    {
        await Assert.ThrowsAsync<CollaborationValidationException>(() =>
            _store.GrantMemberAsync(TenantA, "dave", (CollaborationRole)99, "alice"));
    }

    [Fact]
    public async Task Assessment_ValidatesScoresAndSize()
    {
        await Assert.ThrowsAsync<CollaborationValidationException>(() =>
            _store.PublishAssessmentAsync(TenantA, Submission("analyst") with { Overall = 140 }, "analyst"));

        await Assert.ThrowsAsync<CollaborationValidationException>(() =>
            _store.PublishAssessmentAsync(TenantA, Submission("analyst") with { PayloadJson = "not json" }, "analyst"));

        var store = new CollaborationStore(_factory, _clock, new CollaborationLimits(MaxPayloadBytes: 5));
        await Assert.ThrowsAsync<CollaborationValidationException>(() =>
            store.PublishAssessmentAsync(TenantA, Submission("analyst"), "analyst"));
    }

    [Fact]
    public async Task Plan_CannotBePublishedIntoAnotherTenant()
    {
        var plan = BuildPlan();
        var published = await _store.PublishPlanAsync(TenantA, plan with { TenantId = TenantB }, "designer");

        Assert.Equal(TenantA, published.TenantId);
        Assert.Empty(await _store.ListPlansAsync(TenantB));
        Assert.Single(await _store.ListPlansAsync(TenantA));
    }

    [Fact]
    public async Task ProviderSelection_FailsFastOnUnknownProvider()
    {
        Assert.Throws<ArgumentException>(() => CollaborationStoreFactory.FromConfiguration("Postgres", "Host=localhost"));
        Assert.Throws<ArgumentException>(() => CollaborationStoreFactory.FromConfiguration("Sqlite", ""));

        var sqlServer = CollaborationStoreFactory.FromConfiguration("SqlServer", "Server=.;Database=pgc;Trusted_Connection=True");
        Assert.NotNull(sqlServer);
        Assert.Same(sqlServer, sqlServer);
    }

    private static AssessmentSubmission Submission(string _) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, 72, 70, 74, 68, 30, 12, "{\"findings\":[]}");

    private static ChangePlan BuildPlan()
    {
        var operation = new ChangeOperation(
            "OP-1", ModuleKey.DataLossPrevention, OperationAction.Create,
            "Purview/DLP/baseline", "{\"policy\":\"baseline\"}", false, true);

        var impact = new ChangeImpact(
            "exec", "tech", "business", "users", "security", "compliance", "licensing",
            new[] { "risk" }, new[] { "validate" }, new[] { "rollback" });

        return new ChangePlan(
            Guid.NewGuid(), TenantA, DeploymentMode.GuidedDeployment, "DLP baseline",
            new[] { "REC-1" }, new[] { operation }, impact,
            new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero), "designer", true);
    }

    private sealed class TestFactory : IDbContextFactory<CollaborationDbContext>
    {
        private readonly DbContextOptions<CollaborationDbContext> _options;
        public TestFactory(DbContextOptions<CollaborationDbContext> options) => _options = options;
        public CollaborationDbContext CreateDbContext() => new(_options);
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    }
}
