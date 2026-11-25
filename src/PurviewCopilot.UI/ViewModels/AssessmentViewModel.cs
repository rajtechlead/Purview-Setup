using System.Collections.ObjectModel;
using System.Windows.Input;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Workspace;
using PurviewCopilot.Domain;
using PurviewCopilot.UI.Mvvm;

namespace PurviewCopilot.UI.ViewModels;

public sealed class AssessmentViewModel : PageViewModel
{
    private readonly GovernanceWorkspace _workspace;
    private readonly ISettingsStore _settings;
    private readonly IClock _clock;
    private string _status = "Not started.";
    private string _search = string.Empty;
    private bool _busy;
    private bool _showOnlyGaps = true;

    public AssessmentViewModel(GovernanceWorkspace workspace, ISettingsStore settings, IClock clock)
    {
        _workspace = workspace;
        _settings = settings;
        _clock = clock;

        RunLiveCommand = new AsyncRelayCommand(() => RunAsync(false), () => !Busy);
        RunSampleCommand = new AsyncRelayCommand(() => RunAsync(true), () => !Busy);
    }

    public override string Title => "Assessment";
    public override string Subtitle => "Evidence-based discovery across Purview, Microsoft 365, Entra and security posture.";

    public ObservableCollection<Finding> Findings { get; } = new();
    public ObservableCollection<Recommendation> Recommendations { get; } = new();

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool Busy { get => _busy; private set => SetProperty(ref _busy, value); }
    public bool ShowOnlyGaps { get => _showOnlyGaps; set { if (SetProperty(ref _showOnlyGaps, value)) ApplyFilter(); } }

    public string Search
    {
        get => _search;
        set { if (SetProperty(ref _search, value)) ApplyFilter(); }
    }

    public string LastRun => _workspace.Assessment is null
        ? "No assessment in this session."
        : $"Last run {_workspace.Assessment.RunUtc:yyyy-MM-dd HH:mm} UTC · mode {_workspace.Assessment.Mode}";

    public ICommand RunLiveCommand { get; }
    public ICommand RunSampleCommand { get; }

    public override Task RefreshAsync()
    {
        ApplyFilter();
        Raise(nameof(LastRun));
        return Task.CompletedTask;
    }

    private async Task RunAsync(bool sample)
    {
        Busy = true;
        Status = sample ? "Running assessment against sample data…" : "Discovering tenant. Graph and Purview reads only…";

        try
        {
            if (sample)
            {
                await _workspace.AssessSampleAsync().ConfigureAwait(true);
            }
            else
            {
                var settings = await _settings.LoadAsync().ConfigureAwait(true);

                if (string.IsNullOrWhiteSpace(settings.Auth.TenantId) || string.IsNullOrWhiteSpace(settings.Auth.ClientId))
                {
                    Status = "Complete the connection settings first (Settings → Tenant and application).";
                    return;
                }

                await _workspace.AssessAsync(settings.Auth, settings.DefaultMode).ConfigureAwait(true);
            }

            var assessment = _workspace.Assessment;
            if (assessment is null) return;

            Recommendations.Clear();
            foreach (var recommendation in assessment.Recommendations.Take(50))
            {
                Recommendations.Add(recommendation);
            }

            ApplyFilter();
            Raise(nameof(LastRun));

            Status = $"Completed {assessment.RunUtc:HH:mm:ss} UTC · overall {assessment.Scores.Overall:0.#}/100 · " +
                     $"{assessment.Findings.Count(f => f.Status == FindingStatus.Fail)} failures · " +
                     $"{assessment.Recommendations.Count} recommendations.";
        }
        catch (Exception ex)
        {
            Status = $"Assessment failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private void ApplyFilter()
    {
        Findings.Clear();
        var assessment = _workspace.Assessment;
        if (assessment is null) return;

        var query = assessment.Findings.AsEnumerable();

        if (ShowOnlyGaps)
        {
            query = query.Where(f => f.Status is FindingStatus.Fail or FindingStatus.Warning);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Where(f =>
                f.Id.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                f.Title.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                f.Module.ToString().Contains(Search, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var finding in query.OrderByDescending(f => f.Severity).ThenBy(f => f.Id).Take(300))
        {
            Findings.Add(finding);
        }
    }
}
