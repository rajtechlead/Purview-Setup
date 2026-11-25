using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Infrastructure.Discovery;

/// <summary>
/// Runs Graph discovery first (identity, licensing, posture) then enriches with Purview
/// configuration. A failure in the second stage still yields a usable assessment and records why.
/// </summary>
public sealed class CompositeTenantCollector : ITenantCollector
{
    private readonly GraphTenantCollector _graph;
    private readonly CompliancePowerShellCollector _compliance;

    public CompositeTenantCollector(GraphTenantCollector graph, CompliancePowerShellCollector compliance)
    {
        _graph = graph;
        _compliance = compliance;
    }

    public async Task<TenantSnapshot> CollectAsync(AuthConfiguration auth, CancellationToken cancellationToken = default)
    {
        var snapshot = await _graph.CollectAsync(auth, cancellationToken).ConfigureAwait(false);

        try
        {
            await _compliance.EnrichAsync(snapshot, auth, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            snapshot.Warnings = snapshot.Warnings
                .Concat(new[] { $"Purview enrichment failed: {ex.Message}" })
                .ToList();
        }

        return snapshot;
    }
}
