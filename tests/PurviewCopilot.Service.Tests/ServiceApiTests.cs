using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using PurviewCopilot.Collaboration;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Service.Tests;

public class ServiceApiTests
{
    private const string TenantA = "contoso.onmicrosoft.com";
    private const string TenantB = "fabrikam.onmicrosoft.com";

    private static HttpRequestMessage Request(HttpMethod method, string url, string? user, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (user is not null)
        {
            request.Headers.Add("X-Test-User", user);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    [Fact]
    public async Task Health_IsAnonymous()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("ok", body!.Status);
    }

    [Fact]
    public async Task AnonymousRequest_IsRejected()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(
            Request(HttpMethod.Get, $"/api/tenants/{TenantA}/assessments", user: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotPublishAnAssessment()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();
        await factory.SeedAsync(TenantA, "viewer", CollaborationRole.Viewer);

        var response = await client.SendAsync(Request(
            HttpMethod.Post, $"/api/tenants/{TenantA}/assessments", "viewer", Submission()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Analyst_PublishesAnAssessment_ThatAViewerCanRead()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();
        await factory.SeedAsync(TenantA, "analyst", CollaborationRole.Analyst);
        await factory.SeedAsync(TenantA, "viewer", CollaborationRole.Viewer);

        var created = await client.SendAsync(Request(
            HttpMethod.Post, $"/api/tenants/{TenantA}/assessments", "analyst", Submission()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var listed = await client.SendAsync(Request(
            HttpMethod.Get, $"/api/tenants/{TenantA}/assessments", "viewer"));
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);

        var assessments = await listed.Content.ReadFromJsonAsync<List<SharedAssessmentView>>();
        Assert.Single(assessments!);
        Assert.Equal("analyst", assessments![0].CreatedBy);
    }

    [Fact]
    public async Task Assessment_ValidationReturnsBadRequest()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();
        await factory.SeedAsync(TenantA, "analyst", CollaborationRole.Analyst);

        var response = await client.SendAsync(Request(
            HttpMethod.Post, $"/api/tenants/{TenantA}/assessments", "analyst", Submission() with { Overall = 400 }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AMemberOfAnotherTenant_IsForbidden()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();
        await factory.SeedAsync(TenantA, "alice", CollaborationRole.ApplicationAdministrator);
        await factory.SeedAsync(TenantB, "bob", CollaborationRole.Viewer);

        var response = await client.SendAsync(Request(
            HttpMethod.Get, $"/api/tenants/{TenantA}/assessments", "bob"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlanTenantCannotBeSpoofedThroughTheBody()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();
        await factory.SeedAsync(TenantA, "designer", CollaborationRole.Designer);
        await factory.SeedAsync(TenantB, "admin-b", CollaborationRole.ApplicationAdministrator);

        var spoofed = BuildPlan(TenantB);
        var created = await client.SendAsync(Request(
            HttpMethod.Post, $"/api/tenants/{TenantA}/plans", "designer", spoofed));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var plan = await created.Content.ReadFromJsonAsync<SharedPlanView>();
        Assert.Equal(TenantA, plan!.TenantId);

        var tenantB = await client.SendAsync(Request(
            HttpMethod.Get, $"/api/tenants/{TenantB}/plans", "admin-b"));
        var tenantBPlans = await tenantB.Content.ReadFromJsonAsync<List<SharedPlanView>>();
        Assert.Empty(tenantBPlans!);
    }

    [Fact]
    public async Task ApprovalJourney_RunsThroughAllThreeStages()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();
        await factory.SeedAsync(TenantA, "designer", CollaborationRole.Designer);
        await factory.SeedAsync(TenantA, "operator", CollaborationRole.Operator);
        await factory.SeedAsync(TenantA, "approver", CollaborationRole.Approver);
        await factory.SeedAsync(TenantA, "viewer", CollaborationRole.Viewer);

        var plan = await PublishPlanAsync(client);

        var started = await client.SendAsync(Request(
            HttpMethod.Post, $"/api/tenants/{TenantA}/plans/{plan.Id}/approval/start", "operator"));
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        foreach (var expected in new[] { "PendingCompliance", "PendingCab", "Approved" })
        {
            var decided = await client.SendAsync(Request(
                HttpMethod.Post, $"/api/tenants/{TenantA}/plans/{plan.Id}/approval/decision", "approver",
                new { decision = 0, comment = "ok" }));

            Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
            var approval = await decided.Content.ReadFromJsonAsync<SharedApprovalView>();
            Assert.Equal(expected, approval!.State);
        }

        var read = await client.SendAsync(Request(
            HttpMethod.Get, $"/api/tenants/{TenantA}/plans/{plan.Id}/approval", "viewer"));
        var final = await read.Content.ReadFromJsonAsync<SharedApprovalView>();
        Assert.Equal("Approved", final!.State);
        Assert.Equal(3, final.HistoryCount);
    }

    [Fact]
    public async Task SelfApproval_IsRefusedOverHttp()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();
        await factory.SeedAsync(TenantA, "designer", CollaborationRole.Designer);
        await factory.SeedAsync(TenantA, "operator", CollaborationRole.Operator);

        var plan = await PublishPlanAsync(client);
        await client.SendAsync(Request(
            HttpMethod.Post, $"/api/tenants/{TenantA}/plans/{plan.Id}/approval/start", "operator"));

        // The operator also holds Approver for this test, so only the self-approval rule can stop it.
        await factory.SeedAsync(TenantA, "operator", CollaborationRole.Approver);

        var response = await client.SendAsync(Request(
            HttpMethod.Post, $"/api/tenants/{TenantA}/plans/{plan.Id}/approval/decision", "operator",
            new { decision = 0, comment = "self" }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Self-approval", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StaleIfMatch_IsRejected()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();
        await factory.SeedAsync(TenantA, "designer", CollaborationRole.Designer);
        await factory.SeedAsync(TenantA, "operator", CollaborationRole.Operator);
        await factory.SeedAsync(TenantA, "approver", CollaborationRole.Approver);

        var plan = await PublishPlanAsync(client);
        await client.SendAsync(Request(
            HttpMethod.Post, $"/api/tenants/{TenantA}/plans/{plan.Id}/approval/start", "operator"));

        var request = Request(
            HttpMethod.Post, $"/api/tenants/{TenantA}/plans/{plan.Id}/approval/decision", "approver",
            new { decision = 0, comment = "ok" });
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{Guid.NewGuid()}\""));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Audit_IsReadableByAnAuditorOnly_AndVerifies()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();
        await factory.SeedAsync(TenantA, "analyst", CollaborationRole.Analyst);
        await factory.SeedAsync(TenantA, "auditor", CollaborationRole.Auditor);

        await client.SendAsync(Request(
            HttpMethod.Post, $"/api/tenants/{TenantA}/assessments", "analyst", Submission()));

        var forbidden = await client.SendAsync(Request(
            HttpMethod.Get, $"/api/tenants/{TenantA}/audit", "analyst"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var audited = await client.SendAsync(Request(
            HttpMethod.Get, $"/api/tenants/{TenantA}/audit", "auditor"));
        Assert.Equal(HttpStatusCode.OK, audited.StatusCode);
        var entries = await audited.Content.ReadFromJsonAsync<List<SharedAuditView>>();
        Assert.Contains(entries!, e => e.Action == "AssessmentPublished");

        var verified = await client.SendAsync(Request(
            HttpMethod.Get, $"/api/tenants/{TenantA}/audit/verify", "auditor"));
        var result = await verified.Content.ReadFromJsonAsync<AuditVerification>();
        Assert.True(result!.Valid);
    }

    [Fact]
    public async Task Membership_IsManagedByAnAdministratorOnly()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();
        await factory.SeedAsync(TenantA, "alice", CollaborationRole.ApplicationAdministrator);
        await factory.SeedAsync(TenantA, "analyst", CollaborationRole.Analyst);

        var granted = await client.SendAsync(Request(
            HttpMethod.Post, $"/api/tenants/{TenantA}/members", "alice",
            new { userId = "bob", role = CollaborationRole.Viewer }));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        var bob = await client.SendAsync(Request(
            HttpMethod.Get, $"/api/tenants/{TenantA}/assessments", "bob"));
        Assert.Equal(HttpStatusCode.OK, bob.StatusCode);

        var forbidden = await client.SendAsync(Request(
            HttpMethod.Post, $"/api/tenants/{TenantA}/members", "analyst",
            new { userId = "mallory", role = CollaborationRole.ApplicationAdministrator }));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var lastAdmin = await client.SendAsync(Request(
            HttpMethod.Delete, $"/api/tenants/{TenantA}/members/alice", "alice"));
        Assert.Equal(HttpStatusCode.BadRequest, lastAdmin.StatusCode);
    }

    [Fact]
    public async Task Me_ListsTheCallersTenants()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();
        await factory.SeedAsync(TenantA, "alice", CollaborationRole.ApplicationAdministrator);
        await factory.SeedAsync(TenantB, "alice", CollaborationRole.Viewer);

        var response = await client.SendAsync(Request(HttpMethod.Get, "/api/me", "alice"));
        var me = await response.Content.ReadFromJsonAsync<MeResponse>();

        Assert.Equal("alice", me!.UserId);
        Assert.Equal(2, me.Memberships.Count);
    }

    [Fact]
    public async Task BootstrapAdministrator_CanSignInOnFirstRun()
    {
        using var factory = new BootstrapFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Request(HttpMethod.Get, "/api/me", "bootstrap-admin"));
        var me = await response.Content.ReadFromJsonAsync<MeResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(me!.Memberships);
        Assert.Equal(CollaborationRole.ApplicationAdministrator, me.Memberships[0].Role);
    }

    [Fact]
    public void Production_WithoutEntraConfiguration_RefusesToStart()
    {
        using var factory = new MisconfiguredFactory(setHttps: false, setAuthority: false);

        var error = Assert.ThrowsAny<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Entra authentication is not configured", error.Message);
    }

    [Fact]
    public void Production_WithoutHttps_RefusesToStart()
    {
        using var factory = new MisconfiguredFactory(setHttps: true, setAuthority: true);

        var error = Assert.ThrowsAny<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("HTTPS is required", error.Message);
    }

    private static async Task<SharedPlanView> PublishPlanAsync(HttpClient client)
    {
        var created = await client.SendAsync(Request(
            HttpMethod.Post, $"/api/tenants/{TenantA}/plans", "designer", BuildPlan(TenantA)));
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<SharedPlanView>())!;
    }

    private static AssessmentSubmission Submission() =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, 72, 70, 74, 68, 30, 12, "{\"findings\":[]}");

    private static ChangePlan BuildPlan(string tenantId)
    {
        var operation = new ChangeOperation(
            "OP-1", ModuleKey.DataLossPrevention, OperationAction.Create,
            "Purview/DLP/baseline", "{\"policy\":\"baseline\"}", false, true);

        var impact = new ChangeImpact(
            "exec", "tech", "business", "users", "security", "compliance", "licensing",
            new[] { "risk" }, new[] { "validate" }, new[] { "rollback" });

        return new ChangePlan(
            Guid.NewGuid(), tenantId, DeploymentMode.GuidedDeployment, "DLP baseline",
            new[] { "REC-1" }, new[] { operation }, impact,
            new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero), "designer", true);
    }

    private sealed class BootstrapFactory : ServiceFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Service:BootstrapTenant", TenantA);
            builder.UseSetting("Service:BootstrapAdmin", "bootstrap-admin");
        }
    }

    /// <summary>Starts the real host in Production with deliberately incomplete configuration.</summary>
    private sealed class MisconfiguredFactory : ServiceFactory
    {
        private readonly bool _setHttps;
        private readonly bool _setAuthority;

        public MisconfiguredFactory(bool setHttps, bool setAuthority)
        {
            _setHttps = setHttps;
            _setAuthority = setAuthority;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Collaboration:Provider", "Sqlite");
            builder.UseSetting("ConnectionStrings:Collaboration", $"Data Source={DatabasePath}");
            builder.UseSetting("Service:RequireHttps", _setHttps ? "true" : "false");

            if (_setAuthority)
            {
                builder.UseSetting("AzureAd:Authority", "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/v2.0");
                builder.UseSetting("AzureAd:Audience", "api://purview-copilot");
            }
        }
    }
}
