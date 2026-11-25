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
