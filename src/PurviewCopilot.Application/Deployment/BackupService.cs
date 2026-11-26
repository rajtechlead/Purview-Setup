using System.Text.Json;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

/// <summary>Captures the pre-change evidence package that makes rollback possible.</summary>
public sealed class BackupService
{
    private readonly IBackupStore _store;
    private readonly IClock _clock;

    public BackupService(IBackupStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public Task<ConfigurationBackup?> LatestAsync(string tenantId, CancellationToken cancellationToken = default) =>
        _store.GetLatestAsync(tenantId, cancellationToken);

    /// <summary>Captures the exact operations of one phase, so a phase can be restored alone.</summary>
    public async Task<ConfigurationBackup> CapturePhaseAsync(
        TenantSnapshot snapshot,
        ChangePlan plan,
        DeploymentPhaseDefinition phase,
        IReadOnlyList<ChangeOperation> operations,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            snapshot,
            phase = phase.Phase.ToString(),
            phaseName = phase.Name,
            operations,
            capturedUtc = _clock.UtcNow
        });

        var backup = new ConfigurationBackup(
            Guid.NewGuid(),
            plan.TenantId,
            $"pre-phase/{phase.Phase}/{plan.Id:N}",
            "1.0",
            payload,
            _clock.UtcNow,
            actor);

        await _store.SaveAsync(backup, cancellationToken).ConfigureAwait(false);
        return backup;
    }

    public async Task<ConfigurationBackup> CaptureAsync(
        TenantSnapshot snapshot,
        ChangePlan plan,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            snapshot,
            operations = plan.Operations,
            capturedUtc = _clock.UtcNow
        });

        var backup = new ConfigurationBackup(
            Guid.NewGuid(),
            plan.TenantId,
            $"pre-change/{plan.Id:N}",
            "1.0",
            payload,
            _clock.UtcNow,
            actor);

        await _store.SaveAsync(backup, cancellationToken).ConfigureAwait(false);
        return backup;
    }
}
