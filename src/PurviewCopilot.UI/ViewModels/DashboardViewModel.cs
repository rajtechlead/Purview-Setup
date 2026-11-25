using System.Collections.ObjectModel;
using System.Windows.Input;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Workspace;
using PurviewCopilot.Domain;
using PurviewCopilot.UI.Mvvm;

namespace PurviewCopilot.UI.ViewModels;

public sealed record ScoreCard(string Label, double Value, string Display, string Note);

public sealed class DashboardViewModel : PageViewModel
{
    private readonly GovernanceWorkspace _workspace;
    private readonly IAssessmentStore _store;
    private readonly ISettingsStore _settings;
    private string _tenantName = "No tenant connected";
    private string _collected = "Run a discovery to populate the dashboard.";
    private string _band = "Not assessed";
    private string _warningText = string.Empty;
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
    public ObservableCollection<Finding> PrioritisedFindings { get; } = new();

    public string TenantName { get => _tenantName; private set => SetProperty(ref _tenantName, value); }
    public string Collected { get => _collected; private set => SetProperty(ref _collected, value); }
    public string Band { get => _band; private set => SetProperty(ref _band, value); }
    public string WarningText { get => _warningText; private set => SetProperty(ref _warningText, value); }
    public bool HasData { get => _hasData; private set => SetProperty(ref _hasData, value); }

    public string FailCount => _workspace.Assessment?.Findings.Count(f => f.Status == FindingStatus.Fail).ToString() ?? "0";
    public string WarningCount => _workspace.Assessment?.Findings.Count(f => f.Status == FindingStatus.Warning).ToString() ?? "0";
    public string RecommendationCount => _workspace.Assessment?.Recommendations.Count.ToString() ?? "0";

    public ICommand RefreshCommand { get; }

    public override async Task RefreshAsync()
    {
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
            Scores.Clear();
            Modules.Clear();
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

        PrioritisedFindings.Clear();
        foreach (var finding in assessment.Findings
                     .Where(f => f.Status is FindingStatus.Fail or FindingStatus.Warning)
                     .OrderByDescending(f => f.Severity)
                     .ThenBy(f => f.Id)
                     .Take(12))
        {
            PrioritisedFindings.Add(finding);
        }

        Raise(nameof(FailCount));
        Raise(nameof(WarningCount));
        Raise(nameof(RecommendationCount));
    }
}
