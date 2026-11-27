using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class IrreversibilityGateTests
{
    private static ChangeOperation Operation(string id, Reversibility reversibility, bool destructive = false) =>
        new(id, ModuleKey.DataLifecycle, OperationAction.Delete, $"policy/{id}", "{}", destructive, true, reversibility);

    private static ChangePlan Plan(params ChangeOperation[] operations) =>
        new(Guid.NewGuid(), "tenant", DeploymentMode.GuidedDeployment, "Plan", Array.Empty<string>(), operations,
            new ChangeImpact("", "", "", "", "", "", "", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
            DateTimeOffset.UtcNow, "alice", true);

    private static PhaseExecution Execution() =>
        new(Guid.NewGuid(), Guid.NewGuid(), DeploymentPhase.RecordsManagement, PhaseStatus.Approved, "bob", "Change Manager",
            DateTimeOffset.UtcNow, Array.Empty<string>());

    private static DeploymentAuthorization Authorization(bool riskAccepted) =>
        new(true, true, true, true, true, true, riskAccepted, true, DriftRecheckPerformed: true);

    [Fact]
    public void PlanCountsAndNamesIrreversibleOperations()
    {
        var plan = Plan(
            Operation("keep", Reversibility.Reversible),
            Operation("restore", Reversibility.Restorable),
            Operation("drop", Reversibility.Irreversible),
            Operation("purge", Reversibility.Irreversible));

        Assert.Equal(2, plan.IrreversibleOperationCount);
        Assert.Equal(new[] { "drop", "purge" }, plan.IrreversibleOperationIds);
    }

    [Fact]
    public void GateBlocksIrreversibleOperationsWithoutRiskAcceptance()
    {
        var result = DeploymentGate.Evaluate(
            Plan(Operation("drop", Reversibility.Irreversible)),
            Execution(),
            Authorization(riskAccepted: false));

        Assert.True(result.Blocked);
        Assert.Contains(result.Reasons, r =>
            r.Contains("Irreversible operations", StringComparison.Ordinal) &&
            r.Contains("drop", StringComparison.Ordinal));
    }

    [Fact]
    public void GateAllowsIrreversibleOperationsOnceRiskIsAccepted()
    {
        var result = DeploymentGate.Evaluate(
            Plan(Operation("drop", Reversibility.Irreversible)),
            Execution(),
            Authorization(riskAccepted: true));

        Assert.False(result.Blocked);
    }

    [Fact]
    public void ReversibleOperationsDoNotNeedRiskAcceptance()
    {
        var result = DeploymentGate.Evaluate(
            Plan(Operation("create", Reversibility.Reversible), Operation("update", Reversibility.Restorable)),
            Execution(),
            Authorization(riskAccepted: false));

        Assert.False(result.Blocked);
        Assert.Equal(0, result.Reasons.Count);
    }

    [Fact]
    public void OperationDefaultsToReversibleForOlderPlans()
    {
        var legacy = new ChangeOperation("op", ModuleKey.Audit, OperationAction.Create, "audit", "{}", false, true);

        Assert.Equal(Reversibility.Reversible, legacy.Reversibility);
    }
}
