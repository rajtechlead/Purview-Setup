using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Auth;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

/// <summary>
/// Captures the tenant's real label, label-policy, auto-label, DLP, retention and compliance-tag
/// objects with the read-only export script, then stores the JSON as an encrypted backup payload.
/// </summary>
public sealed class ObjectBackupService
{
    private readonly IPowerShellRunner _runner;
    private readonly BackupService _backups;
    private readonly IClock _clock;

    public ObjectBackupService(IPowerShellRunner runner, BackupService backups, IClock clock)
    {
        _runner = runner;
        _backups = backups;
        _clock = clock;
    }

    public async Task<ConfigurationBackup> CaptureAsync(
        TenantSnapshot tenant,
        ChangePlan plan,
        AuthConfiguration auth,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (auth.Mode != AuthMode.Certificate || string.IsNullOrWhiteSpace(auth.CertificateThumbprint))
            throw new InvalidOperationException("Object backup requires certificate authentication.");

        var path = Path.Combine(Path.GetTempPath(), $"pgc-objects-{Guid.NewGuid():N}.json");
        var profile = CloudProfiles.For(auth.Cloud);

        try
        {
            var script = ConfigurationExportScript.BuildInvocation(
                auth.ClientId,
                auth.CertificateThumbprint,
                tenant.TenantDomain,
                path,
                profile.ComplianceConnectionUri,
                profile.ComplianceAuthorizationUri);

            var result = await _runner.RunAsync(script, new Dictionary<string, string>(), cancellationToken)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Object export failed with exit code {result.ExitCode}: {Truncate(result.Error)}");

            if (!File.Exists(path))
                throw new InvalidOperationException("Object export completed but produced no file.");

            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return await _backups.CaptureExportedObjectsAsync(tenant, plan, json, actor, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string Truncate(string value) => value.Length <= 400 ? value : value[..400];
}
