using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Infrastructure.Deployment;

/// <summary>
/// Restores a phase from its backup. Fails closed unless live execution is explicitly enabled, the
/// credential is certificate-based, the backup belongs to this tenant and the restore plan is ready.
/// </summary>
public sealed class PowerShellRollbackExecutor
{
    private readonly IPowerShellRunner _runner;
    private bool _allowLiveExecution;

    public PowerShellRollbackExecutor(IPowerShellRunner runner, bool allowLiveExecution = false)
    {
        _runner = runner;
        _allowLiveExecution = allowLiveExecution;
    }

    public bool LiveExecutionEnabled => _allowLiveExecution;

    public void SetLiveExecutionEnabled(bool enabled) => _allowLiveExecution = enabled;

    public async Task<ExecutionResult> RestoreAsync(
        ConfigurationBackup backup,
        ChangePlan plan,
        AuthConfiguration auth,
        TenantSnapshot tenant,
        CancellationToken cancellationToken = default)
    {
        if (!_allowLiveExecution)
        {
            return Blocked("Restore execution is disabled in this build. Set an approved executor profile to enable it.");
        }

        if (auth.Mode != AuthMode.Certificate || string.IsNullOrWhiteSpace(auth.CertificateThumbprint))
        {
            return Blocked("Restore requires certificate authentication with an installed thumbprint.");
        }

        if (!string.Equals(backup.TenantId, tenant.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            return Blocked("The backup belongs to a different tenant than the connected one.");
        }

        var restore = RollbackPlanner.Build(backup, plan);
        if (!restore.Ready)
        {
            return new ExecutionResult(false, "Restore blocked: the backup cannot fully reverse this plan.",
                restore.Checks.Where(c => !c.Passed).Select(c => $"BLOCKED {c.Name}: {c.Detail}").ToList());
        }

        var script = RollbackPlanner.BuildScript(plan, restore);
        var environment = new Dictionary<string, string>
        {
            ["PGC_APP_ID"] = auth.ClientId,
            ["PGC_THUMBPRINT"] = auth.CertificateThumbprint!,
            ["PGC_ORG"] = tenant.TenantDomain
        };

        var result = await _runner.RunAsync(script, environment, cancellationToken).ConfigureAwait(false);

        var log = new List<string>
        {
            $"Restore from {backup.Label}: {restore.Steps.Count} step(s).",
            $"PowerShell exit code {result.ExitCode}."
        };

        if (!string.IsNullOrWhiteSpace(result.Output)) log.Add(result.Output.Trim());
        if (!string.IsNullOrWhiteSpace(result.Error)) log.Add(result.Error.Trim());

        return new ExecutionResult(
            result.ExitCode == 0,
            result.ExitCode == 0
                ? "Restore executed. Re-run the assessment to confirm the restored state."
                : $"Restore failed with exit code {result.ExitCode}.",
            log);
    }

    private static ExecutionResult Blocked(string reason) =>
        new(false, reason, new[] { $"BLOCKED {reason}" });
}
