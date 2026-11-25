using System.Collections.ObjectModel;
using System.Windows.Input;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Approvals;
using PurviewCopilot.Application.Workspace;
using PurviewCopilot.Domain;
using PurviewCopilot.UI.Mvvm;

namespace PurviewCopilot.UI.ViewModels;

public sealed class SelectableRecommendation : ObservableObject
{
    private bool _isSelected;

    public SelectableRecommendation(Recommendation recommendation, bool selected)
    {
        Recommendation = recommendation;
        _isSelected = selected;
    }

    public Recommendation Recommendation { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Id => Recommendation.Id;
    public string Title => Recommendation.Title;
    public Severity Priority => Recommendation.Priority;
    public string Effort => Recommendation.Effort;
    public string ChangeRisk => Recommendation.ChangeRisk;
}

public sealed class DeploymentViewModel : PageViewModel
{
    private readonly GovernanceWorkspace _workspace;
    private readonly ISettingsStore _settings;
    private readonly IClock _clock;
    private DeploymentMode _mode = DeploymentMode.GuidedDeployment;
    private string _actor = Environment.UserName;
    private string _approverComment = string.Empty;
    private string _status = "Select recommendations, then build a change plan.";
    private string _planSummary = string.Empty;
    private string _simulationLog = string.Empty;
    private string _approvalState = "No approval request.";
    private bool _busy;

    public DeploymentViewModel(GovernanceWorkspace workspace, ISettingsStore settings, IClock clock)
    {
        _workspace = workspace;
        _settings = settings;
        _clock = clock;

        BuildPlanCommand = new AsyncRelayCommand(BuildPlanAsync, () => !Busy);
        SimulateCommand = new AsyncRelayCommand(SimulateAsync, () => !Busy && Plan is not null);
        SubmitCommand = new AsyncRelayCommand(SubmitAsync, () => !Busy && Plan is not null);
        ApproveCommand = new AsyncRelayCommand(() => DecideAsync(ApprovalDecision.Approve), () => !Busy && Approval is not null);
        RejectCommand = new AsyncRelayCommand(() => DecideAsync(ApprovalDecision.Reject), () => !Busy && Approval is not null);
        RequestChangesCommand = new AsyncRelayCommand(() => DecideAsync(ApprovalDecision.RequestChanges), () => !Busy && Approval is not null);
    }

    public override string Title => "Deployment";
    public override string Subtitle => "Review, simulate, approve and apply — with a rollback package for every plan.";

    public ObservableCollection<SelectableRecommendation> Candidates { get; } = new();

    public IReadOnlyList<DeploymentMode> Modes { get; } = Enum.GetValues<DeploymentMode>();

    public DeploymentMode Mode { get => _mode; set => SetProperty(ref _mode, value); }
    public string Actor { get => _actor; set => SetProperty(ref _actor, value); }
    public string ApproverComment { get => _approverComment; set => SetProperty(ref _approverComment, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string PlanSummary { get => _planSummary; private set => SetProperty(ref _planSummary, value); }
    public string SimulationLog { get => _simulationLog; private set => SetProperty(ref _simulationLog, value); }
    public string ApprovalState { get => _approvalState; private set => SetProperty(ref _approvalState, value); }
    public bool Busy { get => _busy; private set => SetProperty(ref _busy, value); }
    public ChangePlan? Plan { get; private set; }
    public ApprovalRequest? Approval { get; private set; }

    public ICommand BuildPlanCommand { get; }
    public ICommand SimulateCommand { get; }
    public ICommand SubmitCommand { get; }
    public ICommand ApproveCommand { get; }
    public ICommand RejectCommand { get; }
    public ICommand RequestChangesCommand { get; }

    public override async Task RefreshAsync()
    {
        var settings = await _settings.LoadAsync().ConfigureAwait(true);
        Mode = settings.DefaultMode;

        Candidates.Clear();
        var assessment = _workspace.Assessment;
        if (assessment is null)
        {
            Status = "Run an assessment first — deployment is driven by its recommendations.";
            return;
        }

        foreach (var recommendation in assessment.Recommendations.Take(80))
        {
            Candidates.Add(new SelectableRecommendation(recommendation, recommendation.Priority >= Severity.High));
        }

        Status = $"{Candidates.Count} recommendation(s) available. High and critical items are preselected.";
    }

    private async Task BuildPlanAsync()
    {
        var selected = Candidates.Where(c => c.IsSelected).Select(c => c.Recommendation).ToList();
        if (selected.Count == 0)
        {
            Status = "Select at least one recommendation.";
            return;
        }

        Busy = true;
        try
        {
            Plan = await _workspace.CreatePlanAsync(selected, Mode, Actor).ConfigureAwait(true);

            var impact = Plan.Impact;
            PlanSummary =
                $"{impact.ExecutiveSummary}\n\n" +
                $"Technical: {impact.TechnicalSummary}\n\n" +
                $"Business: {impact.BusinessImpact}\n\n" +
                $"User: {impact.UserImpact}\n\n" +
                $"Security: {impact.SecurityImpact}\n\n" +
                $"Compliance: {impact.ComplianceImpact}\n\n" +
                $"Licensing: {impact.LicensingImpact}\n\n" +
                $"Rollback: {string.Join(" ", impact.RollbackSteps)}";

            Approval = null;
            ApprovalState = "Plan created. Simulation is required before approval.";
            Status = $"Change plan {Plan.Id:N} created with {Plan.Operations.Count} operation(s) and a pre-change backup.";
        }
        catch (Exception ex)
        {
            Status = $"Plan creation failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task SimulateAsync()
    {
        if (Plan is null) return;

        Busy = true;
        try
        {
            var result = await _workspace.SimulateAsync(Plan).ConfigureAwait(true);
            SimulationLog = string.Join(Environment.NewLine, result.Log);
            Status = result.Summary;
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task SubmitAsync()
    {
        if (Plan is null) return;

        Busy = true;
        try
        {
            Approval = await _workspace.StartApprovalAsync(Plan, Actor).ConfigureAwait(true);
            ApprovalState = $"Submitted · stage {ApprovalWorkflow.CurrentStage(Approval)} (state {Approval.State}).";
            Status = "Approval request submitted to the Security stage.";
        }
        catch (Exception ex)
        {
            Status = $"Submission failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task DecideAsync(ApprovalDecision decision)
    {
        if (Approval is null) return;

        Busy = true;
        try
        {
            Approval = await _workspace.DecideAsync(Approval, decision, Actor, ApproverComment).ConfigureAwait(true);
            ApprovalState = decision == ApprovalDecision.Approve
                ? $"State {Approval.State} · next stage {ApprovalWorkflow.CurrentStage(Approval)}."
                : $"State {Approval.State}.";
            Status = decision == ApprovalDecision.Approve
                ? "Approval recorded. Continue through Compliance and CAB before applying."
                : $"Decision recorded: {decision}.";
            ApproverComment = string.Empty;
        }
        catch (Exception ex)
        {
            Status = $"Decision failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }
}
