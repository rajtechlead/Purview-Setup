using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

/// <summary>Evidence the operator must present before a phase may touch the tenant.</summary>
public sealed record DeploymentAuthorization(
    bool SimulationPassed,
    bool Approved,
    bool BackupCaptured,
    bool RollbackReady,
    bool AuthenticationValidated,
    bool ConnectivityOk,
    bool RiskAccepted);

public sealed record DeploymentGateResult(bool Blocked, IReadOnlyList<string> Reasons)
{
    public static DeploymentGateResult Allow { get; } = new(false, Array.Empty<string>());
}

/// <summary>Automatic deployment protection: every unsafe condition blocks execution.</summary>
public static class DeploymentGate
{
    public static DeploymentGateResult Evaluate(ChangePlan plan, PhaseExecution execution, DeploymentAuthorization authorization)
    {
        var reasons = new List<string>();

        if (!authorization.SimulationPassed) reasons.Add("Simulation has not passed.");
        if (!authorization.Approved) reasons.Add("Phase approval is missing (separation of duties).");
        if (!authorization.BackupCaptured) reasons.Add("The pre-phase backup is missing.");
        if (!authorization.RollbackReady) reasons.Add("The rollback package is missing.");
        if (!authorization.AuthenticationValidated) reasons.Add("Authentication has not been validated.");
        if (!authorization.ConnectivityOk) reasons.Add("Tenant connectivity has not been validated.");
        if (plan.DestructiveOperationCount > 0 && !authorization.RiskAccepted) reasons.Add("Destructive operations require explicit risk acceptance.");

        if (execution.Status is PhaseStatus.Failed or PhaseStatus.Paused or PhaseStatus.RolledBack)
            reasons.Add($"The phase is {execution.Status} and must be reset before execution.");

        if (execution.Status != PhaseStatus.Approved)
            reasons.Add($"The phase must be approved before execution (current: {execution.Status}).");

        return reasons.Count == 0 ? DeploymentGateResult.Allow : new DeploymentGateResult(true, reasons);
    }
}
