using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Auth;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

/// <summary>
/// One-phase-at-a-time production rollout. A phase is simulated, explicitly approved by someone
/// other than the operator, backed up, executed, validated and only then may the next phase start.
/// </summary>
public sealed class PhasedDeploymentService
{
    private readonly IChangeExecutor _executor;
    private readonly BackupService _backups;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public PhasedDeploymentService(IChangeExecutor executor, BackupService backups, IAuditLog audit, IClock clock)
    {
        _executor = executor;
        _backups = backups;
        _audit = audit;
        _clock = clock;
    }

    public PhaseExecution Prepare(ChangePlan plan, DeploymentPhase phase, string actor)
    {
        var definition = DeploymentPhases.For(phase);
        var operations = DeploymentPhases.OperationsFor(plan, phase);

        return new PhaseExecution(
            Guid.NewGuid(),
            plan.Id,
            phase,
            PhaseStatus.NotStarted,
            string.Empty,
            string.Empty,
            _clock.UtcNow,
            new[]
            {
                $"{definition.Name}: {operations.Count} operation(s) selected by {actor}.",
                definition.Description
            });
    }

    public async Task<(PhaseExecution Execution, ExecutionResult Result)> SimulateAndBackupAsync(
        ChangePlan plan,
        PhaseExecution execution,
        TenantSnapshot snapshot,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (execution.Status is not (PhaseStatus.NotStarted or PhaseStatus.Failed))
            throw new InvalidOperationException($"A phase in state {execution.Status} cannot be simulated again.");

        var operations = DeploymentPhases.OperationsFor(plan, execution.Phase);
        var phasePlan = plan with { Operations = operations, SimulationRequired = true };

        var simulation = await _executor.SimulateAsync(phasePlan, cancellationToken).ConfigureAwait(false);
        if (!simulation.Succeeded)
        {
            return (Append(execution with { Status = PhaseStatus.Failed }, simulation.Log), simulation);
        }

        var backup = await _backups.CapturePhaseAsync(
            snapshot, plan, DeploymentPhases.For(execution.Phase), operations, actor, cancellationToken)
            .ConfigureAwait(false);

        var updated = Append(execution with { Status = PhaseStatus.Simulated }, simulation.Log)
            .Log.Append($"Backup {backup.Id} captured ({backup.Label}).")
            .ToList();

        await _audit.WriteAsync(new AuditEntry(
            Guid.NewGuid(), _clock.UtcNow, actor, "DeploymentPhaseSimulated", plan.Id.ToString(),
            $"Phase={execution.Phase}; Operations={operations.Count}; Backup={backup.Id}", null), cancellationToken)
            .ConfigureAwait(false);

        return (execution with { Status = PhaseStatus.Simulated, UpdatedUtc = _clock.UtcNow, Log = updated }, simulation);
    }

    public async Task<PhaseExecution> ApproveAsync(
        PhaseExecution execution,
        string operatorName,
        string approver,
        string approverRole,
        CancellationToken cancellationToken = default)
    {
        if (execution.Status != PhaseStatus.Simulated)
            throw new InvalidOperationException("The phase must be simulated and backed up before approval.");

        SeparationOfDuties.Validate(operatorName, approver, approverRole);

        var updated = Append(execution with
        {
            Status = PhaseStatus.Approved,
            ApprovedBy = approver,
            ApproverRole = approverRole,
            UpdatedUtc = _clock.UtcNow
        }, new[] { $"Approved by {approver} ({approverRole}). Separation of duties validated." });

        await _audit.WriteAsync(new AuditEntry(
            Guid.NewGuid(), _clock.UtcNow, approver, "DeploymentPhaseApproved", execution.PlanId.ToString(),
            $"Phase={execution.Phase}; Operator={operatorName}; Role={approverRole}", null), cancellationToken)
            .ConfigureAwait(false);

        return updated;
    }

    public async Task<PhaseExecution> RecordExecutionAsync(
        PhaseExecution execution, ExecutionResult result, string actor, CancellationToken cancellationToken = default)
    {
        var status = result.Succeeded ? PhaseStatus.Executed : PhaseStatus.Failed;
        var updated = Append(execution with { Status = status, UpdatedUtc = _clock.UtcNow }, result.Log);

        await _audit.WriteAsync(new AuditEntry(
            Guid.NewGuid(), _clock.UtcNow, actor, $"DeploymentPhase{status}", execution.PlanId.ToString(),
            $"Phase={execution.Phase}; {result.Summary}", null), cancellationToken).ConfigureAwait(false);

        return updated;
    }

    public async Task<PhaseExecution> RecordValidationAsync(
        PhaseExecution execution, ValidationReport report, string actor, CancellationToken cancellationToken = default)
    {
        var status = report.Passed ? PhaseStatus.Validated : PhaseStatus.Failed;
        var log = report.Checks.Select(c => $"{(c.Passed ? "PASS" : "FAIL")} {c.Name}: {c.Detail}").ToArray();
        var updated = Append(execution with { Status = status, UpdatedUtc = _clock.UtcNow }, log);

        await _audit.WriteAsync(new AuditEntry(
            Guid.NewGuid(), _clock.UtcNow, actor, $"DeploymentPhase{status}", execution.PlanId.ToString(),
            $"Phase={execution.Phase}; {report.Summary}", null), cancellationToken).ConfigureAwait(false);

        return updated;
    }

    public PhaseExecution Pause(PhaseExecution execution, string reason) =>
        Append(execution with { Status = PhaseStatus.Paused, UpdatedUtc = _clock.UtcNow }, new[] { $"Paused: {reason}" });

    public PhaseExecution MarkRolledBack(PhaseExecution execution, ConfigurationBackup backup) =>
        Append(execution with { Status = PhaseStatus.RolledBack, UpdatedUtc = _clock.UtcNow },
            new[] { $"Rolled back from backup {backup.Id} ({backup.Label}). Re-run the assessment to confirm restored state." });

    public static void EnsurePreviousValidated(DeploymentPhase phase, PhaseExecution? previous)
    {
        if (phase == DeploymentPhase.Discovery) return;

        if (previous is null || previous.Status != PhaseStatus.Validated)
            throw new InvalidOperationException("A phase can start only after the previous phase has been validated.");
    }

    private static PhaseExecution Append(PhaseExecution execution, IEnumerable<string> lines) =>
        execution with
        {
            UpdatedUtc = DateTimeOffset.UtcNow,
            Log = execution.Log.Concat(lines).ToList()
        };
}
