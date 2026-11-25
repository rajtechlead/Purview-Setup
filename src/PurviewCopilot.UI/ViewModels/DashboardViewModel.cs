using System.Collections.ObjectModel;
using System.Windows.Input;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Monitoring;
using PurviewCopilot.Application.Workspace;
using PurviewCopilot.Domain;
using PurviewCopilot.UI.Mvvm;

namespace PurviewCopilot.UI.ViewModels;

public sealed record ScoreCard(string Label, double Value, string Display, string Note);

public sealed record HeatmapCellView(string Module, string Severity, int Count, double Opacity);

public sealed record TrendBar(string Label, double Height, string Value);

public sealed class DashboardViewModel : PageViewModel
{
    private static readonly ModuleKey[] SecurityKeys =
    {
        ModuleKey.Audit, ModuleKey.DataLossPrevention, ModuleKey.EndpointDlp, ModuleKey.InsiderRisk,
        ModuleKey.InformationProtection, ModuleKey.SensitivityLabels, ModuleKey.AutoLabeling, ModuleKey.DataClassification
    };

    private static readonly ModuleKey[] GovernanceKeys =
    {
        ModuleKey.DataLifecycle, ModuleKey.RecordsManagement, ModuleKey.Dspm,
        ModuleKey.AiGovernance, ModuleKey.CopilotGovernance, ModuleKey.ComplianceManager
    };

    private static readonly ModuleKey[] ComplianceKeys =
    {
        ModuleKey.Audit, ModuleKey.DataLossPrevention, ModuleKey.RecordsManagement, ModuleKey.DataLifecycle,
        ModuleKey.CommunicationCompliance, ModuleKey.EDIscovery, ModuleKey.ComplianceManager, ModuleKey.SensitivityLabels
    };

    private readonly GovernanceWorkspace _workspace;
    private readonly IAssessmentStore _store;
    private readonly ISettingsStore _settings;
    private string _tenantName = "No tenant connected";
    private string _collected = "Run a discovery to populate the dashboard.";
    private string _band = "Not assessed";
    private string _maturityTrail = "No history yet.";
    private string _overallDelta = "No previous assessment.";
    private string _warningText = string.Empty;
    private string _securitySummary = "No assessment.";
    private string _governanceSummary = "No assessment.";
    private string _complianceSummary = "No assessment.";
    private string _portfolioHint = "Loading customer tenants…";
    private bool _hasData;

    public DashboardViewModel(GovernanceWorkspace workspace, IAssessmentStore store, ISettingsStore settings)
    {
        _workspace = workspace;
        _store = store;
        _settings = settings;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public override string Title => "Executive Dashboard";
    public override string Subtitle => "Security, governance, compliance and AI readiness in one view.";

    public ObservableCollection<ScoreCard> Scores { get; } = new();
    public ObservableCollection<ModuleScore> Modules { get; } = new();
    public ObservableCollection<ModuleScore> SecurityModules { get; } = new();
    public ObservableCollection<ModuleScore> GovernanceModules { get; } = new();
    public ObservableCollection<ModuleScore> ComplianceModules { get; } = new();
    public ObservableCollection<Finding> PrioritisedFindings { get; } = new();
    public ObservableCollection<HeatmapCellView> Heatmap { get; } = new();
    public ObservableCollection<TrendBar> Trend { get; } = new();
    public ObservableCollection<FrameworkCoverage> Frameworks { get; } = new();
    public ObservableCollection<PortfolioRow> Portfolio { get; } = new();

    public string TenantName { get => _tenantName; private set => SetProperty(ref _tenantName, value); }
    public string Collected { get => _collected; private set => SetProperty(ref _collected, value); }
    public string Band { get => _band; private set => SetProperty(ref _band, value); }
    public string MaturityTrail { get => _maturityTrail; private set => SetProperty(ref _maturityTrail, value); }
    public string OverallDelta { get => _overallDelta; private set => SetProperty(ref _overallDelta, value); }
    public string WarningText { get => _warningText; private set => SetProperty(ref _warningText, value); }
    public string SecuritySummary { get => _securitySummary; private set => SetProperty(ref _securitySummary, value); }
    public string GovernanceSummary { get => _governanceSummary; private set => SetProperty(ref _governanceSummary, value); }
    public string ComplianceSummary { get => _complianceSummary; private set => SetProperty(ref _complianceSummary, value); }
    public string PortfolioHint { get => _portfolioHint; private set => SetProperty(ref _portfolioHint, value); }
    public bool HasData { get => _hasData; private set => SetProperty(ref _hasData, value); }

    public string FailCount => _workspace.Assessment?.Findings.Count(f => f.Status == FindingStatus.Fail).ToString() ?? "0";
    public string WarningCount => _workspace.Assessment?.Findings.Count(f => f.Status == FindingStatus.Warning).ToString() ?? "0";
    public string RecommendationCount => _workspace.Assessment?.Recommendations.Count.ToString() ?? "0";

    public ICommand RefreshCommand { get; }

    public override async Task RefreshAsync()
    {
        await LoadPortfolioAsync().ConfigureAwait(true);

        var assessment = _workspace.Assessment;

        if (assessment is null)
        {
            var settings = await _settings.LoadAsync().ConfigureAwait(true);
            assessment = await _store.GetLatestAsync(settings.Auth.TenantId).ConfigureAwait(true);
        }

        if (assessment is null)
        {
            HasData = false;
            TenantName = "No tenant connected";
            Collected = "Run Assessment → Discover tenant to populate the dashboard.";
            Band = "Not assessed";
            MaturityTrail = "No history yet.";
            OverallDelta = "No previous assessment.";
            Scores.Clear();
            Modules.Clear();
            ClearPersonaViews();
            return;
        }

        HasData = true;
        var tenant = _workspace.Tenant;
        TenantName = tenant?.TenantName ?? assessment.TenantId;
        Collected = $"Assessment {assessment.RunUtc:yyyy-MM-dd HH:mm} UTC · {assessment.Findings.Count} controls · {assessment.RuleCount} rules";
        Band = assessment.Scores.MaturityLevel.ToString();
        WarningText = tenant is null || tenant.Warnings.Count == 0
            ? string.Empty
            : $"Discovery notes: {string.Join(" · ", tenant.Warnings.Take(4))}";

        Scores.Clear();
        Scores.Add(new ScoreCard("Overall", assessment.Scores.Overall, $"{assessment.Scores.Overall:0.#}", assessment.Scores.Band));
        Scores.Add(new ScoreCard("Security", assessment.Scores.Security, $"{assessment.Scores.Security:0.#}", "Protection controls"));
        Scores.Add(new ScoreCard("Governance", assessment.Scores.Governance, $"{assessment.Scores.Governance:0.#}", "Lifecycle and AI"));
        Scores.Add(new ScoreCard("Compliance", assessment.Scores.Compliance, $"{assessment.Scores.Compliance:0.#}", "Regulatory coverage"));
        Scores.Add(new ScoreCard("Risk", assessment.Scores.Risk, $"{assessment.Scores.Risk:0.#}", "Lower is better"));
        Scores.Add(new ScoreCard("AI governance", assessment.Scores.AiGovernance, $"{assessment.Scores.AiGovernance:0.#}", "DSPM and policy"));
        Scores.Add(new ScoreCard("Copilot readiness", assessment.Scores.CopilotReadiness, $"{assessment.Scores.CopilotReadiness:0.#}", "Pre-rollout controls"));

        Modules.Clear();
        foreach (var module in assessment.Scores.Modules.OrderByDescending(m => m.Assessed))
        {
            Modules.Add(module);
        }

        Fill(SecurityModules, SecurityKeys);
        Fill(GovernanceModules, GovernanceKeys);
        Fill(ComplianceModules, ComplianceKeys);

        PrioritisedFindings.Clear();
        foreach (var finding in assessment.Findings
                     .Where(f => f.Status is FindingStatus.Fail or FindingStatus.Warning)
                     .OrderByDescending(f => f.Severity)
                     .ThenBy(f => f.Id)
                     .Take(12))
        {
            PrioritisedFindings.Add(finding);
        }

        Heatmap.Clear();
        foreach (var cell in RiskHeatmap.Build(assessment.Findings))
        {
            Heatmap.Add(new HeatmapCellView(
                cell.Module.ToString(),
                cell.Severity.ToString(),
                cell.Count,
                Math.Min(1, 0.25 + cell.Count * 0.18)));
        }

        Frameworks.Clear();
        foreach (var framework in _workspace.Frameworks)
        {
            Frameworks.Add(framework);
        }

        SecuritySummary = Summary(assessment.Scores.Security, SecurityKeys, assessment.Findings);
        GovernanceSummary = Summary(assessment.Scores.Governance, GovernanceKeys, assessment.Findings);
        ComplianceSummary = Summary(assessment.Scores.Compliance, ComplianceKeys, assessment.Findings);

        var history = await _workspace.RefreshHistoryAsync(assessment.TenantId).ConfigureAwait(true);

        Trend.Clear();
        var points = TrendSeries.Build(history, 12);
        foreach (var point in points)
        {
            Trend.Add(new TrendBar(
                point.RunUtc.ToLocalTime().ToString("MM-dd"),
                8 + Math.Round(point.Overall * 0.72, 1),
                $"{point.Overall:0}"));
        }

        if (history.Count > 1)
        {
            var older = history[1].Scores;
            MaturityTrail = $"{older.MaturityLevel} → {assessment.Scores.MaturityLevel} over {history.Count} assessments";
            var delta = assessment.Scores.Overall - older.Overall;
            OverallDelta = $"{delta:+0.#;-0.#;0} points vs previous assessment";
        }
        else
        {
            MaturityTrail = $"{assessment.Scores.MaturityLevel} · baseline assessment";
            OverallDelta = "Baseline assessment — no comparison yet.";
        }

        Raise(nameof(FailCount));
        Raise(nameof(WarningCount));
        Raise(nameof(RecommendationCount));
    }

    private async Task LoadPortfolioAsync()
    {
        try
        {
            var rows = await _workspace.BuildPortfolioAsync().ConfigureAwait(true);

            Portfolio.Clear();
            foreach (var row in rows)
            {
                Portfolio.Add(row);
            }

            PortfolioHint = rows.Count == 0
                ? "No customer tenants saved. Save the current connection in Settings → Customer tenants."
                : $"{rows.Count} customer tenant(s) · {rows.Count(r => r.Assessed)} assessed";
        }
        catch (Exception ex)
        {
            PortfolioHint = $"Portfolio unavailable: {ex.Message}";
        }
    }

    private void Fill(ObservableCollection<ModuleScore> target, IReadOnlyCollection<ModuleKey> keys)
    {
        target.Clear();
        foreach (var module in Modules.Where(m => keys.Contains(m.Module)))
        {
            target.Add(module);
        }
    }

    private void ClearPersonaViews()
    {
        SecurityModules.Clear();
        GovernanceModules.Clear();
        ComplianceModules.Clear();
        Heatmap.Clear();
        Trend.Clear();
        Frameworks.Clear();
        SecuritySummary = "No assessment.";
        GovernanceSummary = "No assessment.";
        ComplianceSummary = "No assessment.";
    }

    private static string Summary(double score, IReadOnlyCollection<ModuleKey> keys, IReadOnlyList<Finding> findings)
    {
        var open = findings.Count(f =>
            keys.Contains(f.Module) && f.Status is FindingStatus.Fail or FindingStatus.Warning);
        var critical = findings.Count(f =>
            keys.Contains(f.Module) && f.Severity >= Severity.High && f.Status is FindingStatus.Fail or FindingStatus.Warning);

        return $"{score:0.#}/100 · {open} open finding(s) · {critical} high or critical";
    }
}
