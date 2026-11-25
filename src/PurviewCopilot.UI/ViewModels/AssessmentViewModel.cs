using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Workspace;
using PurviewCopilot.Domain;
using PurviewCopilot.UI.Mvvm;

namespace PurviewCopilot.UI.ViewModels;

public sealed class ActionRow
{
    public ActionRow(Finding finding, FindingAction? action)
    {
        Finding = finding;
        Action = action;
    }

    public Finding Finding { get; }
    public FindingAction? Action { get; }
    public string FindingId => Finding.Id;
    public string Title => Finding.Title;
    public string Module => Finding.Module.ToString();
    public string Severity => Finding.Severity.ToString();
    public string OwnerText => string.IsNullOrWhiteSpace(Action?.Owner) ? "Unassigned" : Action!.Owner;
    public string StatusText => Action?.Status.ToString() ?? nameof(ActionStatus.Open);
    public string DueText => Action?.DueUtc is { } due ? due.ToString("yyyy-MM-dd") : "—";
}

public sealed class AssessmentViewModel : PageViewModel
{
    private readonly GovernanceWorkspace _workspace;
    private readonly ISettingsStore _settings;
    private readonly IFindingActionStore _actions;
    private readonly IClock _clock;
    private string _status = "Not started.";
    private string _search = string.Empty;
    private ActionRow? _selectedActionRow;
    private string _actionOwner = string.Empty;
    private string _actionDueText = string.Empty;
    private string _actionNotes = string.Empty;
    private ActionStatus _actionStatus = ActionStatus.Open;
    private string _actionStatusMessage = "Run an assessment to populate the action centre.";
    private bool _busy;
    private bool _showOnlyGaps = true;

    public AssessmentViewModel(
        GovernanceWorkspace workspace,
        ISettingsStore settings,
        IFindingActionStore actions,
        IClock clock)
    {
        _workspace = workspace;
        _settings = settings;
        _actions = actions;
        _clock = clock;

        RunLiveCommand = new AsyncRelayCommand(() => RunAsync(false), () => !Busy);
        RunSampleCommand = new AsyncRelayCommand(() => RunAsync(true), () => !Busy);
        SaveActionCommand = new AsyncRelayCommand(SaveActionAsync);
    }

    public override string Title => "Assessment";
    public override string Subtitle => "Evidence-based discovery across Purview, Microsoft 365, Entra and security posture.";

    public ObservableCollection<Finding> Findings { get; } = new();
    public ObservableCollection<Recommendation> Recommendations { get; } = new();
    public ObservableCollection<ActionRow> ActionRows { get; } = new();
    public IReadOnlyList<ActionStatus> ActionStatuses { get; } = Enum.GetValues<ActionStatus>();

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

    public string ActionOwner { get => _actionOwner; set => SetProperty(ref _actionOwner, value); }
    public string ActionDueText { get => _actionDueText; set => SetProperty(ref _actionDueText, value); }
    public string ActionNotes { get => _actionNotes; set => SetProperty(ref _actionNotes, value); }
    public ActionStatus SelectedActionStatus { get => _actionStatus; set => SetProperty(ref _actionStatus, value); }
    public string ActionStatusMessage { get => _actionStatusMessage; private set => SetProperty(ref _actionStatusMessage, value); }

    public ActionRow? SelectedActionRow
    {
        get => _selectedActionRow;
        set
        {
            if (!SetProperty(ref _selectedActionRow, value)) return;

            ActionOwner = value?.Action?.Owner ?? string.Empty;
            ActionDueText = value?.Action?.DueUtc?.ToString("yyyy-MM-dd") ?? string.Empty;
            SelectedActionStatus = value?.Action?.Status ?? ActionStatus.Open;
            ActionNotes = value?.Action?.Notes ?? string.Empty;
        }
    }

    public ICommand RunLiveCommand { get; }
    public ICommand RunSampleCommand { get; }
    public ICommand SaveActionCommand { get; }

    public override async Task RefreshAsync()
    {
        ApplyFilter();
        await LoadActionsAsync().ConfigureAwait(true);
        Raise(nameof(LastRun));
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
            await LoadActionsAsync().ConfigureAwait(true);
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

    private async Task LoadActionsAsync()
    {
        ActionRows.Clear();
        var assessment = _workspace.Assessment;

        if (assessment is null)
        {
            SelectedActionRow = null;
            ActionStatusMessage = "Run an assessment to populate the action centre.";
            return;
        }

        var stored = (await _actions.ListAsync(assessment.TenantId).ConfigureAwait(true))
            .ToDictionary(a => a.FindingId, StringComparer.OrdinalIgnoreCase);

        foreach (var finding in assessment.Findings
                     .Where(f => f.Status is FindingStatus.Fail or FindingStatus.Warning)
                     .OrderByDescending(f => f.Severity)
                     .ThenBy(f => f.Id))
        {
            stored.TryGetValue(finding.Id, out var action);
            ActionRows.Add(new ActionRow(finding, action));
        }

        var previousId = SelectedActionRow?.FindingId;
        SelectedActionRow = ActionRows.FirstOrDefault(r =>
                                string.Equals(r.FindingId, previousId, StringComparison.OrdinalIgnoreCase))
                            ?? ActionRows.FirstOrDefault();

        ActionStatusMessage = ActionRows.Count == 0
            ? "No open findings to assign."
            : $"{ActionRows.Count(r => r.Action is not null)} of {ActionRows.Count} open findings have an assignment recorded.";
    }

    private async Task SaveActionAsync()
    {
        var row = SelectedActionRow;
        var assessment = _workspace.Assessment;

        if (row is null || assessment is null)
        {
            ActionStatusMessage = "Select an open finding first.";
            return;
        }

        DateTimeOffset? due = null;
        if (!string.IsNullOrWhiteSpace(ActionDueText))
        {
            if (!DateTimeOffset.TryParse(ActionDueText, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsed))
            {
                ActionStatusMessage = "Due date must be a date such as 2026-12-31.";
                return;
            }

            due = new DateTimeOffset(parsed.Date, parsed.Offset);
        }

        var action = new FindingAction(
            row.FindingId,
            assessment.TenantId,
            ActionOwner.Trim(),
            due,
            SelectedActionStatus,
            ActionNotes.Trim(),
            _clock.UtcNow);

        try
        {
            await _actions.SaveAsync(action).ConfigureAwait(true);
            await LoadActionsAsync().ConfigureAwait(true);

            ActionStatusMessage =
                $"Saved {action.FindingId}: {action.Status} · " +
                $"{(string.IsNullOrWhiteSpace(action.Owner) ? "unassigned" : action.Owner)}" +
                (due is null ? "." : $" · due {due:yyyy-MM-dd}.");
        }
        catch (Exception ex)
        {
            ActionStatusMessage = $"Action save failed: {ex.Message}";
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
