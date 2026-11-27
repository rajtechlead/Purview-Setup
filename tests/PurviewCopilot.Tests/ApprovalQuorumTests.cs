using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Approvals;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class ApprovalQuorumTests
{
    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    }

    private static ApprovalRequest Submitted(IClock clock, string requester = "operator") =>
        ApprovalWorkflow.Submit(ApprovalWorkflow.Start(Guid.NewGuid(), clock), requester, clock, "hash");

    [Fact]
    public void SecurityQuorum_AdvancesOnlyAfterTheSecondDistinctApproval()
    {
        var clock = new Clock();
        var policy = new ApprovalPolicy(SecurityApprovals: 2);
        var request = Submitted(clock);

        request = ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "alice", "ok", clock, "hash", policy);

        Assert.Equal(ApprovalState.PendingSecurity, request.State);
        Assert.Single(request.History);

        request = ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "bob", "ok", clock, "hash", policy);

        Assert.Equal(ApprovalState.PendingCompliance, request.State);
        Assert.Equal(2, request.History.Count);
    }

    [Fact]
    public void OneActor_CannotSatisfyAQuorumTwice()
    {
        var clock = new Clock();
        var policy = new ApprovalPolicy(SecurityApprovals: 2);
        var request = ApprovalWorkflow.Decide(
            Submitted(clock), ApprovalDecision.Approve, "alice", "ok", clock, "hash", policy);

        var error = Assert.Throws<InvalidOperationException>(() =>
            ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "Alice", "ok again", clock, "hash", policy));

        Assert.Contains("distinct approvers", error.Message);
        Assert.Single(request.History);
    }

    [Fact]
    public void QuorumIsConfiguredPerStage()
    {
        var clock = new Clock();
        var policy = new ApprovalPolicy(SecurityApprovals: 1, ComplianceApprovals: 2, CabApprovals: 1);
        var request = Submitted(clock);

        request = ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "alice", "", clock, "hash", policy);
        Assert.Equal(ApprovalState.PendingCompliance, request.State);

        request = ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "carol", "", clock, "hash", policy);
        Assert.Equal(ApprovalState.PendingCompliance, request.State);

        request = ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "dave", "", clock, "hash", policy);
        Assert.Equal(ApprovalState.PendingCab, request.State);

        request = ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "erin", "", clock, "hash", policy);
        Assert.Equal(ApprovalState.Approved, request.State);
    }

    [Fact]
    public void RequestChanges_ResetsTheQuorumRound()
    {
        var clock = new Clock();
        var policy = new ApprovalPolicy(SecurityApprovals: 2);
        var request = Submitted(clock);

        request = ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "alice", "", clock, "hash", policy);
        request = ApprovalWorkflow.Decide(request, ApprovalDecision.RequestChanges, "bob", "revise", clock, "hash", policy);
        Assert.Equal(ApprovalState.Draft, request.State);

        request = ApprovalWorkflow.Submit(request, "operator", clock, "hash");
        request = ApprovalWorkflow.Decide(request, ApprovalDecision.Approve, "alice", "", clock, "hash", policy);

        // The pre-rework approval must not count towards the new round.
        Assert.Equal(ApprovalState.PendingSecurity, request.State);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 10)]
    public void InvalidQuorumIsRejected(int security, int compliance, int cab)
    {
        var clock = new Clock();
        var policy = new ApprovalPolicy(security, compliance, cab);

        var error = Assert.Throws<InvalidOperationException>(() =>
            ApprovalWorkflow.Decide(Submitted(clock), ApprovalDecision.Approve, "alice", "", clock, "hash", policy));

        Assert.Contains("quorum must be between 1 and", error.Message);
    }

    [Fact]
    public void DefaultPolicyRemainsOneApproverPerStage()
    {
        Assert.Equal(1, ApprovalPolicy.Default.For(ApprovalStage.Security));
        Assert.Equal(1, ApprovalPolicy.Default.For(ApprovalStage.Compliance));
        Assert.Equal(1, ApprovalPolicy.Default.For(ApprovalStage.Cab));
    }
}
