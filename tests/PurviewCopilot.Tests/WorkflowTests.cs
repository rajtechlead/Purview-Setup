using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Approvals;
using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Application.Licensing;
using PurviewCopilot.Application.Demo;
using PurviewCopilot.Application.Monitoring;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class WorkflowTests
{
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
    }

    private static Recommendation MakeRecommendation(string id, string moduleOps = "op-1") =>
        new(id, ModuleKey.DataLossPrevention, $"Remediate {id}", "evidence", Severity.High, "Low", "Low",
            new[] { "E3" },
            new[]
            {
                new ChangeOperation(moduleOps, ModuleKey.DataLossPrevention, OperationAction.Create,
                    $"Purview/DLP/{moduleOps}", "{\"policy\":\"baseline\"}", false, true)
            });

    [Fact]
    public void Approval_RequiresThreeStages_ThenApproves()
    {
        var clock = new TestClock();
        var request = ApprovalWorkflow.Start(Guid.NewGuid(), clock);
        request = ApprovalWorkflow.Submit(request, "operator", clock);

        Assert.Equal(ApprovalState.PendingSecurity, request.State);
        Assert.Equal(ApprovalStage.Security, ApprovalWorkflow.CurrentStage(request));

        request = ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "security", "ok", clock);
        Assert.Equal(ApprovalState.PendingCompliance, request.State);

        request = ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "compliance", "ok", clock);
        Assert.Equal(ApprovalState.PendingCab, request.State);

        request = ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "cab", "ok", clock);
        Assert.Equal(ApprovalState.Approved, request.State);
        Assert.Equal(3, request.History.Count);
    }

    [Fact]
    public void Approval_RejectionEndsRequest()
    {
        var clock = new TestClock();
        var request = ApprovalWorkflow.Submit(ApprovalWorkflow.Start(Guid.NewGuid(), clock), "operator", clock);
        request = ApprovalWorkflow.Decide(request, ApprovalDecision.Reject, "security", "no", clock);

        Assert.Equal(ApprovalState.Rejected, request.State);
        Assert.Throws<InvalidOperationException>(() =>
            ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "x", "", clock));
    }

    [Fact]
    public void Approval_RequestChangesReturnsToDraft()
    {
        var clock = new TestClock();
        var request = ApprovalWorkflow.Submit(ApprovalWorkflow.Start(Guid.NewGuid(), clock), "operator", clock);
        request = ApprovalWorkflow.Decide(request, ApprovalDecision.RequestChanges, "security", "adjust", clock);

        Assert.Equal(ApprovalState.Draft, request.State);
        Assert.Equal(0, request.CurrentStageIndex);
    }

    [Fact]
    public void Approval_BlocksSelfApproval()
    {
        var clock = new MutableClock();
        var request = ApprovalWorkflow.Submit(
            ApprovalWorkflow.Start(Guid.NewGuid(), clock), "alice", clock, "hash-one");

        var error = Assert.Throws<InvalidOperationException>(() =>
            ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "ALICE", "ok", clock, "hash-one"));

        Assert.Contains("Self-approval", error.Message);
    }

    [Fact]
    public void Approval_RejectsAnExpiredRequest()
    {
        var clock = new MutableClock();
        var request = ApprovalWorkflow.Submit(
            ApprovalWorkflow.Start(Guid.NewGuid(), clock), "alice", clock, "hash-one", TimeSpan.FromHours(1));

        clock.UtcNow = clock.UtcNow.AddHours(2);

        var error = Assert.Throws<InvalidOperationException>(() =>
            ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "bob", "ok", clock, "hash-one"));

        Assert.Contains("expired", error.Message);
    }

    [Fact]
    public void Approval_IsInvalidatedWhenThePlanChanges()
    {
        var clock = new MutableClock();
        var request = ApprovalWorkflow.Submit(
            ApprovalWorkflow.Start(Guid.NewGuid(), clock), "alice", clock, "hash-one");

        var error = Assert.Throws<InvalidOperationException>(() =>
            ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "bob", "ok", clock, "hash-two"));

        Assert.Contains("invalidated", error.Message);
    }

    [Fact]
    public void Approval_RecordsHashRequesterAndExpiry()
    {
        var clock = new MutableClock();
        var started = ApprovalWorkflow.Start(Guid.NewGuid(), clock);
        var request = ApprovalWorkflow.Submit(started, "alice", clock, "hash-one");

        Assert.Equal("hash-one", request.PlanHash);
        Assert.Equal("alice", request.RequestedBy);
        Assert.Equal(clock.UtcNow + ApprovalWorkflow.DefaultApprovalLifetime, request.ExpiresUtc);

        var decided = ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "bob", "ok", clock, "hash-one");
        Assert.Equal("hash-one", Assert.Single(decided.History).PlanHash);
        Assert.Equal("bob", decided.History[0].Actor);
    }

    [Fact]
    public void ChangePlanHasher_IsStableAndDetectsPayloadEdits()
    {
        var plan = new ChangePlanBuilder(new TestClock()).Build(
            DemoTenant.Create(), DeploymentMode.GuidedDeployment,
            new[] { MakeRecommendation("REC-1") }, "operator");

        Assert.Equal(ChangePlanHasher.Compute(plan), ChangePlanHasher.Compute(plan));

        var edited = plan with
        {
            Operations = plan.Operations
                .Select(o => o with { PayloadJson = "{\"policy\":\"changed\"}" })
                .ToList()
        };

        Assert.NotEqual(ChangePlanHasher.Compute(plan), ChangePlanHasher.Compute(edited));
    }

    [Fact]
    public void ChangePlan_DeduplicatesOperations_AndBuildsImpact()
    {
        var builder = new ChangePlanBuilder(new TestClock());
        var plan = builder.Build(
            DemoTenant.Create(),
            DeploymentMode.GuidedDeployment,
            new[] { MakeRecommendation("REC-1"), MakeRecommendation("REC-2"), MakeRecommendation("REC-3") },
            "operator");

        Assert.Single(plan.Operations);
        Assert.False(string.IsNullOrWhiteSpace(plan.Impact.ExecutiveSummary));
        Assert.NotEmpty(plan.Impact.RollbackSteps);
        Assert.NotEmpty(plan.Impact.ValidationSteps);
    }

    [Fact]
    public void ChangePlan_RejectsEmptySelection()
    {
        var builder = new ChangePlanBuilder(new TestClock());
        Assert.Throws<ArgumentException>(() => builder.Build(
            DemoTenant.Create(), DeploymentMode.Custom,
            Array.Empty<Recommendation>(), "operator"));
    }

    [Fact]
    public void LicenseOptimizer_FlagsE5GapAndUnusedSeats()
    {
        var snapshot = new TenantSnapshot
        {
            Licenses = new[]
            {
                new LicenseSku("SPE_E3", 80, 100),
                new LicenseSku("COPILOT_M365", 10, 10)
            },
            CopilotLicenses = 10,
            SensitivityLabelCount = 2
        };

        var insights = LicenseOptimizer.Analyze(snapshot);

        Assert.Contains(insights, i => i.Id == "LIC-E5-GAP");
        Assert.Contains(insights, i => i.Id == "LIC-UNUSED-SPE_E3");
        Assert.Contains(insights, i => i.Id == "LIC-COPILOT-LABELS");
    }

    [Fact]
    public void LicenseOptimizer_HandlesEmptyInventory()
    {
        var insights = LicenseOptimizer.Analyze(new TenantSnapshot());
        Assert.Contains(insights, i => i.Id == "LIC-000");
    }

    [Fact]
    public void LicenseOptimizer_ComputesUtilization()
    {
        Assert.Equal(50, LicenseOptimizer.UtilizationPercent(new LicenseSku("SPE_E5", 5, 10)), 1);
        Assert.Equal(0, LicenseOptimizer.UtilizationPercent(new LicenseSku("SPE_E5", 0, 0)), 1);
    }

    [Fact]
    public void Drift_DetectsAuditDisabledAndDlpRegression()
    {
        var baseline = DemoTenant.Create();
        baseline.UnifiedAuditEnabled = true;
        baseline.DlpEnforcedCount = 2;

        var current = DemoTenant.Create();
        current.UnifiedAuditEnabled = false;
        current.DlpEnforcedCount = 0;

        var drift = DriftDetector.Compare(baseline, current);

        Assert.Contains(drift, d => d.Id == "DRF-001");
        Assert.Contains(drift, d => d.Id == "DRF-004");
        Assert.True(DriftDetector.DriftSeverityScore(drift) > 0);
    }

    [Fact]
    public void Drift_NoChange_ProducesNoItems()
    {
        var snapshot = DemoTenant.Create();
        Assert.Empty(DriftDetector.Compare(snapshot, snapshot.Clone()));
    }
}

internal static class SnapshotExtensions
{
    public static TenantSnapshot Clone(this TenantSnapshot source) => new()
    {
        TenantId = source.TenantId,
        TenantName = source.TenantName,
        TenantDomain = source.TenantDomain,
        UnifiedAuditEnabled = source.UnifiedAuditEnabled,
        AuditRetentionDays = source.AuditRetentionDays,
        SensitivityLabelCount = source.SensitivityLabelCount,
        DlpEnforcedCount = source.DlpEnforcedCount,
        EndpointDlpEnabled = source.EndpointDlpEnabled,
        DevicesOnboarded = source.DevicesOnboarded,
        GlobalAdministrators = source.GlobalAdministrators,
        ConditionalAccessPolicies = source.ConditionalAccessPolicies,
        AppRegistrationsWithSecrets = source.AppRegistrationsWithSecrets,
        RetentionLabelCount = source.RetentionLabelCount,
        SecureScorePercent = source.SecureScorePercent,
        Licenses = source.Licenses,
        CopilotLicenses = source.CopilotLicenses
    };
}
