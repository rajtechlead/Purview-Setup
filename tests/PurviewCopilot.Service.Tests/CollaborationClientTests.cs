using PurviewCopilot.Collaboration;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Service.Tests;

public class CollaborationClientTests
{
    private const string Tenant = "contoso.onmicrosoft.com";

    private static CollaborationClient Client(ServiceFactory factory, string user)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Test-User", user);
        return new CollaborationClient(http);
    }

    private static AssessmentSubmission Submission() =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, 81, 78, 84, 80, 76, 5, "{\"tenant\":\"contoso\"}");

    [Fact]
    public async Task AnalystPublishes_ViewerReadsThroughTheClient()
    {
        using var factory = new ServiceFactory();
        await factory.SeedAsync(Tenant, "analyst", CollaborationRole.Analyst);
        await factory.SeedAsync(Tenant, "viewer", CollaborationRole.Viewer);

        var published = await Client(factory, "analyst").PublishAssessmentAsync(Tenant, Submission());
        var visible = await Client(factory, "viewer").ListAssessmentsAsync(Tenant);

        Assert.Equal(Tenant, published.TenantId);
        Assert.Contains(visible, a => a.Id == published.Id);
    }

    [Fact]
    public async Task ViewerPublish_IsForbiddenWithAnActionableMessage()
    {
        using var factory = new ServiceFactory();
        await factory.SeedAsync(Tenant, "viewer", CollaborationRole.Viewer);

        var error = await Assert.ThrowsAsync<CollaborationClientException>(
            () => Client(factory, "viewer").PublishAssessmentAsync(Tenant, Submission()));

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Contains("role does not permit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingCredential_IsReportedAsSignInRequired()
    {
        using var factory = new ServiceFactory();

        var anonymous = new CollaborationClient(factory.CreateClient());
        var error = await Assert.ThrowsAsync<CollaborationClientException>(() => anonymous.GetMeAsync());

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Contains("Sign in", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdentityListsMemberships()
    {
        using var factory = new ServiceFactory();
        await factory.SeedAsync(Tenant, "operator", CollaborationRole.Operator);

        var me = await Client(factory, "operator").GetMeAsync();

        Assert.Equal("operator", me.UserId);
        Assert.Contains(me.Memberships, m => m.TenantId == Tenant && m.Role == CollaborationRole.Operator);
    }

    [Fact]
    public async Task ApprovalCanBeStartedRead_ThenDecidedWithTheStamp()
    {
        using var factory = new ServiceFactory();
        await factory.SeedAsync(Tenant, "designer", CollaborationRole.Designer);
        await factory.SeedAsync(Tenant, "operator", CollaborationRole.Operator);
        await factory.SeedAsync(Tenant, "approver", CollaborationRole.Approver);

        var designer = Client(factory, "designer");
        var plan = await designer.PublishPlanAsync(Tenant, Plan());
        var started = await Client(factory, "operator").StartApprovalAsync(Tenant, plan.Id, "ready for review");
        var loaded = await designer.GetApprovalAsync(Tenant, plan.Id);

        Assert.Equal(started.Id, loaded!.Id);

        var decided = await Client(factory, "approver").DecideAsync(
            Tenant, plan.Id, ApprovalDecision.Approve, "looks good", loaded.ConcurrencyStamp);

        // Approval is staged: the first decision moves the request to compliance review.
        Assert.Equal("PendingCompliance", decided.State);
    }

    [Fact]
    public async Task AuditVerifiesAndListsChainedEntries()
    {
        using var factory = new ServiceFactory();
        await factory.SeedAsync(Tenant, "analyst", CollaborationRole.Analyst);
        await factory.SeedAsync(Tenant, "auditor", CollaborationRole.Auditor);

        await Client(factory, "analyst").PublishAssessmentAsync(Tenant, Submission());
        var client = Client(factory, "auditor");
        var entries = await client.ListAuditAsync(Tenant);
        var verification = await client.VerifyAuditAsync(Tenant);

        Assert.NotEmpty(entries);
        Assert.True(verification.Valid);
        Assert.True(verification.Checked >= entries.Count);
    }

    [Fact]
    public async Task MemberManagementRoundTrips()
    {
        using var factory = new ServiceFactory();
        await factory.SeedAsync(Tenant, "admin", CollaborationRole.ApplicationAdministrator);

        var client = Client(factory, "admin");
        await client.GrantMemberAsync(Tenant, "new-viewer", CollaborationRole.Viewer);
        var members = await client.ListMembersAsync(Tenant);

        Assert.Contains(members, m => m.UserId == "new-viewer" && m.Role == CollaborationRole.Viewer);

        await client.RemoveMemberAsync(Tenant, "new-viewer");
        members = await client.ListMembersAsync(Tenant);
        Assert.DoesNotContain(members, m => m.UserId == "new-viewer");
    }

    [Fact]
    public async Task UnknownPlanApproval_ReturnsNullInsteadOfThrowing()
    {
        using var factory = new ServiceFactory();
        await factory.SeedAsync(Tenant, "viewer", CollaborationRole.Viewer);

        Assert.Null(await Client(factory, "viewer").GetApprovalAsync(Tenant, Guid.NewGuid()));
    }

    private static ChangePlan Plan() =>
        new(Guid.NewGuid(), Tenant, DeploymentMode.GuidedDeployment, "Client plan", Array.Empty<string>(),
            new[] { new ChangeOperation("audit-enable", ModuleKey.Audit, OperationAction.Update, "audit", "{}", false, true) },
            new ChangeImpact("", "", "", "", "", "", "", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
            DateTimeOffset.UtcNow, "designer", true);
}
