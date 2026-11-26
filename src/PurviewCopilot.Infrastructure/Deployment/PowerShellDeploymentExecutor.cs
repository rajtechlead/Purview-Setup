using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Infrastructure.Deployment;

/// <summary>
/// Gated live executor. Fails closed unless the host explicitly enabled live execution, the phase
/// carries a real approval, and every gate item (simulation, backup, rollback, auth, connectivity,
/// risk) is satisfied. Scripts use certificate authentication and never include secret material.
/// </summary>
public sealed class PowerShellDeploymentExecutor : ILiveDeploymentExecutor
{
    private readonly IPowerShellRunner _runner;
    private bool _allowLiveExecution;

    public PowerShellDeploymentExecutor(IPowerShellRunner runner, bool allowLiveExecution = false)
    {
        _runner = runner;
        _allowLiveExecution = allowLiveExecution;
    }

    public bool LiveExecutionEnabled => _allowLiveExecution;

    public void SetLiveExecutionEnabled(bool enabled) => _allowLiveExecution = enabled;

    public string BuildPreview(ChangePlan plan) => ChangeExecutor.BuildScript(plan);

    public async Task<ExecutionResult> ExecuteAsync(
        ChangePlan plan,
        PhaseExecution execution,
        AuthConfiguration auth,
        TenantSnapshot tenant,
        DeploymentAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        if (!_allowLiveExecution)
        {
            return Blocked("Live execution is disabled in this build. Set an approved executor profile to enable it.");
        }

        if (auth.Mode != AuthMode.Certificate || string.IsNullOrWhiteSpace(auth.CertificateThumbprint))
        {
            return Blocked("Live execution requires certificate authentication with an installed thumbprint.");
        }

        if (execution.Phase == DeploymentPhase.Discovery)
        {
            return new ExecutionResult(true, "Discovery phase is read-only; nothing was executed.",
                new[] { "No operations in the discovery phase." });
        }

        if (execution.Phase is not (DeploymentPhase.Discovery) &&
            !plan.Operations.Any(o => DeploymentPhases.For(execution.Phase).Modules.Contains(o.Module)))
        {
            return new ExecutionResult(true, "No operations are mapped to this phase.",
                new[] { "Nothing to execute." });
        }

        var gate = DeploymentGate.Evaluate(plan, execution, authorization);
        if (gate.Blocked)
        {
            return new ExecutionResult(false, "Deployment blocked by the safety gate.",
                gate.Reasons.Select(r => $"BLOCKED {r}").ToList());
        }

        var operations = DeploymentPhases.OperationsFor(plan, execution.Phase);
        var script = ChangeExecutor.BuildScript(plan with { Operations = operations, SimulationRequired = true });
        var environment = new Dictionary<string, string>
        {
            ["PGC_APP_ID"] = auth.ClientId,
            ["PGC_THUMBPRINT"] = auth.CertificateThumbprint!,
            ["PGC_ORG"] = tenant.TenantDomain
        };

        var result = await _runner.RunAsync(script, environment, cancellationToken).ConfigureAwait(false);
        var log = new List<string>
        {
            $"Phase {execution.Phase}: {operations.Count} operation(s).",
            $"PowerShell exit code {result.ExitCode}."
        };

        if (!string.IsNullOrWhiteSpace(result.Output)) log.Add(result.Output.Trim());
        if (!string.IsNullOrWhiteSpace(result.Error)) log.Add(result.Error.Trim());

        return new ExecutionResult(
            result.ExitCode == 0,
            result.ExitCode == 0
                ? $"Phase {execution.Phase} executed. Validate against a fresh assessment before continuing."
                : $"Phase {execution.Phase} failed with exit code {result.ExitCode}.",
            log);
    }

    private static ExecutionResult Blocked(string reason) =>
        new(false, reason, new[] { $"BLOCKED {reason}" });
}
