using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PurviewCopilot.Application.Approvals;
using PurviewCopilot.Collaboration;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Service.Tests;

public class ServiceQuorumTests
{
    private const string Tenant = "contoso.onmicrosoft.com";

    private static HttpRequestMessage Request(HttpMethod method, string url, string user, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-User", user);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static ChangePlan Plan() =>
        new(Guid.NewGuid(), Tenant, DeploymentMode.GuidedDeployment, "Quorum plan", Array.Empty<string>(),
            new[] { new ChangeOperation("audit", ModuleKey.Audit, OperationAction.Update, "audit", "{}", false, true) },
            new ChangeImpact("", "", "", "", "", "", "", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
            DateTimeOffset.UtcNow, "designer", true);

    [Fact]
    public async Task ServiceRequiresTheConfiguredNumberOfDistinctApprovers()
    {
        using var factory = new ServiceFactory(("Collaboration:Approval:Security", "2"));
        using var client = factory.CreateClient();

        await factory.SeedAsync(Tenant, "designer", CollaborationRole.Designer);
        await factory.SeedAsync(Tenant, "operator", CollaborationRole.Operator);
        await factory.SeedAsync(Tenant, "alice", CollaborationRole.Approver);
        await factory.SeedAsync(Tenant, "bob", CollaborationRole.Approver);

        var created = await client.SendAsync(Request(HttpMethod.Post, $"/api/tenants/{Tenant}/plans", "designer", Plan()));
        created.EnsureSuccessStatusCode();
        var plan = await created.Content.ReadFromJsonAsync<SharedPlanView>();

        var start = await client.SendAsync(Request(
            HttpMethod.Post, $"/api/tenants/{Tenant}/plans/{plan!.Id}/approval/start", "operator", new { comment = "" }));
        start.EnsureSuccessStatusCode();

        var first = await client.SendAsync(Request(
            HttpMethod.Post, $"/api/tenants/{Tenant}/plans/{plan.Id}/approval/decision", "alice",
            new { decision = ApprovalDecision.Approve, comment = "ok" }));
        first.EnsureSuccessStatusCode();
        Assert.Equal("PendingSecurity", (await first.Content.ReadFromJsonAsync<SharedApprovalView>())!.State);

        var second = await client.SendAsync(Request(
            HttpMethod.Post, $"/api/tenants/{Tenant}/plans/{plan.Id}/approval/decision", "bob",
            new { decision = ApprovalDecision.Approve, comment = "ok" }));
        second.EnsureSuccessStatusCode();
        Assert.Equal("PendingCompliance", (await second.Content.ReadFromJsonAsync<SharedApprovalView>())!.State);
    }

    [Fact]
    public void InvalidQuorumConfigurationFailsStartupParsing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Collaboration:Approval:Security"] = "0"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => ServiceDefaults.ReadApprovalPolicy(configuration));
    }

    [Fact]
    public void QuorumDefaultsToOneApproverPerStage()
    {
        var policy = ServiceDefaults.ReadApprovalPolicy(new ConfigurationBuilder().Build());

        Assert.Equal(ApprovalPolicy.Default, policy);
    }
}
