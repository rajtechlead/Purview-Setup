using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Modules;
using PurviewCopilot.Domain;
using PurviewCopilot.Domain.Scoring;

namespace PurviewCopilot.Application.Assessment;

/// <summary>
/// Runs discovery, assessment and recommendation generation in one auditable pass.
/// Findings are produced only from collected evidence — a module never guesses.
/// </summary>
public sealed class AssessmentService
{
    private readonly ITenantCollector _collector;
    private readonly ModuleRegistry _registry;
    private readonly IAssessmentStore _store;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public AssessmentService(
        ITenantCollector collector,
        ModuleRegistry registry,
        IAssessmentStore store,
        IAuditLog audit,
        IClock clock)
    {
        _collector = collector;
        _registry = registry;
        _store = store;
        _audit = audit;
        _clock = clock;
    }

    public async Task<AssessmentResult> RunAsync(
        AuthConfiguration auth,
        DeploymentMode mode = DeploymentMode.AssessmentOnly,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _collector.CollectAsync(auth, cancellationToken).ConfigureAwait(false);
        return await RunAsync(snapshot, mode, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AssessmentResult> RunAsync(
        TenantSnapshot snapshot,
        DeploymentMode mode = DeploymentMode.AssessmentOnly,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();
        var recommendations = new List<Recommendation>();

        foreach (var module in _registry.Modules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var moduleFindings = await module.AssessAsync(snapshot, cancellationToken).ConfigureAwait(false);
            findings.AddRange(moduleFindings);

            var moduleRecommendations = await module
                .RecommendAsync(snapshot, moduleFindings, cancellationToken)
                .ConfigureAwait(false);
            recommendations.AddRange(moduleRecommendations);
        }

        var scores = ScoringEngine.Compute(findings, _registry.Weights);
        var result = new AssessmentResult(
            Guid.NewGuid(),
            snapshot.TenantId,
            _clock.UtcNow,
            mode.ToString(),
            scores,
            findings,
            recommendations.OrderByDescending(r => r.Priority).ToList(),
            ModuleCatalog.All.Count);

        await _store.SaveAsync(result, cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(new AuditEntry(
            Guid.NewGuid(),
            _clock.UtcNow,
            "assessment-service",
            "AssessmentCompleted",
            snapshot.TenantId,
            $"Findings={findings.Count}; Fail={findings.Count(f => f.Status == FindingStatus.Fail)}; Overall={scores.Overall:0.##}",
            result.Id.ToString()), cancellationToken).ConfigureAwait(false);

        return result;
    }
}
