using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Approvals;
using PurviewCopilot.Application.Assessment;
using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Application.Demo;
using PurviewCopilot.Application.Licensing;
using PurviewCopilot.Application.Monitoring;
using PurviewCopilot.Application.Reporting;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Workspace;

/// <summary>
/// Use-case facade for the UI. Holds session state only in memory; all durable state is written
/// through ports so the same logic works in tests and in a headless CLI.
/// </summary>
public sealed class GovernanceWorkspace
{
    private readonly ITenantCollector _collector;
    private readonly AssessmentService _assessmentService;
    private readonly ChangePlanBuilder _planBuilder;
    private readonly BackupService _backups;
    private readonly IChangePlanStore _planStore;
    private readonly IApprovalStore _approvalStore;
    private readonly IReportExporter[] _exporters;
    private readonly ISettingsStore _settings;
    private readonly IChangeExecutor _executor;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    private TenantSnapshot? _lastCollected;
    private TenantSnapshot? _baseline;

    public GovernanceWorkspace(
        ITenantCollector collector,
        AssessmentService assessmentService,
        ChangePlanBuilder planBuilder,
        BackupService backups,
        IChangePlanStore planStore,
        IApprovalStore approvalStore,
        IEnumerable<IReportExporter> exporters,
        ISettingsStore settings,
        IChangeExecutor executor,
        IAuditLog audit,
        IClock clock)
    {
        _collector = collector;
        _assessmentService = assessmentService;
        _planBuilder = planBuilder;
        _backups = backups;
        _planStore = planStore;
        _approvalStore = approvalStore;
        _exporters = exporters.ToArray();
        _settings = settings;
        _executor = executor;
        _audit = audit;
        _clock = clock;
    }

    public TenantSnapshot? Tenant => _lastCollected;
    public AssessmentResult? Assessment { get; private set; }
    public IReadOnlyList<FrameworkCoverage> Frameworks =>
        Assessment is null ? Array.Empty<FrameworkCoverage>() : FrameworkMapper.Map(Assessment.Findings);

    public IReadOnlyList<LicenseInsight> LicenseInsights =>
        _lastCollected is null ? Array.Empty<LicenseInsight>() : LicenseOptimizer.Analyze(_lastCollected);

    public IReadOnlyList<DriftItem> Drift =>
        _baseline is null || _lastCollected is null
            ? Array.Empty<DriftItem>()
            : DriftDetector.Compare(_baseline, _lastCollected);

    public async Task<AssessmentResult> AssessAsync(
        AuthConfiguration auth,
        DeploymentMode mode,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _collector.CollectAsync(auth, cancellationToken).ConfigureAwait(false);

        if (_lastCollected is not null)
        {
            _baseline = _lastCollected;
        }

        _lastCollected = snapshot;
        Assessment = await _assessmentService.RunAsync(snapshot, mode, cancellationToken).ConfigureAwait(false);
        return Assessment;
    }

    public async Task<AssessmentResult> AssessSampleAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = DemoTenant.Create();
        _lastCollected = snapshot;
        Assessment = await _assessmentService.RunAsync(snapshot, DeploymentMode.AssessmentOnly, cancellationToken)
            .ConfigureAwait(false);
        return Assessment;
    }

    public async Task<ChangePlan> CreatePlanAsync(
        IReadOnlyList<Recommendation> recommendations,
        DeploymentMode mode,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (_lastCollected is null)
            throw new InvalidOperationException("Run an assessment before creating a change plan.");

        var plan = _planBuilder.Build(_lastCollected, mode, recommendations, actor);
        await _backups.CaptureAsync(_lastCollected, plan, actor, cancellationToken).ConfigureAwait(false);
        await _planStore.SaveAsync(plan, cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(new AuditEntry(
            Guid.NewGuid(), _clock.UtcNow, actor, "ChangePlanCreated", plan.Id.ToString(),
            $"Operations={plan.Operations.Count}; Destructive={plan.DestructiveOperationCount}", null), cancellationToken)
            .ConfigureAwait(false);

        return plan;
    }

    public async Task<ApprovalRequest> StartApprovalAsync(ChangePlan plan, string actor, CancellationToken cancellationToken = default)
    {
        var request = ApprovalWorkflow.Start(plan.Id, _clock);
        request = ApprovalWorkflow.Submit(request, actor, _clock);
        await _approvalStore.SaveAsync(request, cancellationToken).ConfigureAwait(false);
        return request;
    }

    public async Task<ApprovalRequest> DecideAsync(
        ApprovalRequest request,
        ApprovalDecision decision,
        string actor,
        string comment,
        CancellationToken cancellationToken = default)
    {
        var updated = ApprovalWorkflow.Decide(request, decision, actor, comment, _clock);
        await _approvalStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(new AuditEntry(
            Guid.NewGuid(), _clock.UtcNow, actor, $"Approval{decision}", request.PlanId.ToString(),
            $"Stage={ApprovalWorkflow.CurrentStage(request)}; NewState={updated.State}", null), cancellationToken)
            .ConfigureAwait(false);

        await _planStore.UpdateStateAsync(updated.PlanId, updated.State.ToString(), cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public Task<ExecutionResult> SimulateAsync(ChangePlan plan, CancellationToken cancellationToken = default) =>
        _executor.SimulateAsync(plan, cancellationToken);

    public Task<ExecutionResult> ApplyAsync(ChangePlan plan, CancellationToken cancellationToken = default) =>
        _executor.ApplyAsync(plan, cancellationToken);

    /// <summary>Configuration-as-code export: a portable JSON package for the plan.</summary>
    public string ExportPlanJson(ChangePlan plan) => ConfigurationPackage.Export(plan, _clock.UtcNow);

    /// <summary>Configuration-as-code export: executable PowerShell for the plan.</summary>
    public string GeneratePlanScript(ChangePlan plan) => _executor.GenerateScript(plan);

    /// <summary>Configuration-as-code export: reversal script with destructive steps commented out.</summary>
    public string GenerateRollbackScript(ChangePlan plan) => _executor.GenerateRollbackScript(plan);

    /// <summary>Configuration-as-code import. Returns a new plan that must be simulated and approved again.</summary>
    public ChangePlan ImportPlan(string json) => ConfigurationPackage.Import(json, _clock.UtcNow);

    public string RenderReport(ReportFormat format, string organization, string generatedBy)
    {
        if (Assessment is null || _lastCollected is null)
            throw new InvalidOperationException("Run an assessment before generating a report.");

        var exporter = _exporters.FirstOrDefault(e => e.Format == format)
                       ?? throw new NotSupportedException($"No exporter registered for {format}.");

        return exporter.Render(new ReportContext(
            _lastCollected,
            Assessment,
            organization,
            generatedBy,
            _clock.UtcNow,
            Frameworks));
    }

    public async Task<string> ExportReportAsync(
        ReportFormat format,
        string path,
        string organization,
        string generatedBy,
        CancellationToken cancellationToken = default)
    {
        var exporter = _exporters.FirstOrDefault(e => e.Format == format)
                       ?? throw new NotSupportedException($"No exporter registered for {format}.");

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await File.WriteAllTextAsync(path, RenderReport(format, organization, generatedBy), cancellationToken)
            .ConfigureAwait(false);

        await _audit.WriteAsync(new AuditEntry(
            Guid.NewGuid(), _clock.UtcNow, generatedBy, "ReportExported", path,
            $"Format={format}; Bytes={new FileInfo(path).Length}", null), cancellationToken).ConfigureAwait(false);

        return path;
    }

    public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
        _settings.LoadAsync(cancellationToken);
}
