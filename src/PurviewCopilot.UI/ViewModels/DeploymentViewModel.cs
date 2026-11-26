using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Approvals;
using PurviewCopilot.Application.Auth;
using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Application.Workspace;
using PurviewCopilot.Domain;
using PurviewCopilot.Infrastructure.Auth;
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
    private readonly PhasedDeploymentService _phased;
    private readonly ILiveDeploymentExecutor _liveExecutor;
    private readonly ConnectionVerifier _verifier;
    private DeploymentPhaseDefinition _selectedPhase = DeploymentPhases.All[0];
    private PhaseExecution? _phaseExecution;
    private bool _phaseSimulated;
    private string _phaseApprover = string.Empty;
    private string _phaseApproverRole = "Change Manager";
    private bool _phaseRiskAccepted;
    private string _phaseStatusText = "No phase prepared.";
    private string _phaseLog = string.Empty;
    private DeploymentMode _mode = DeploymentMode.GuidedDeployment;
    private string _actor = Environment.UserName;
    private string _approverComment = string.Empty;
    private string _status = "Select recommendations, then build a change plan.";
    private string _planSummary = string.Empty;
    private string _simulationLog = string.Empty;
    private string _approvalState = "No approval request.";
    private bool _busy;

    public DeploymentViewModel(
        GovernanceWorkspace workspace,
        ISettingsStore settings,
        IClock clock,
        PhasedDeploymentService phased,
        ILiveDeploymentExecutor liveExecutor,
        ConnectionVerifier verifier)
    {
        _workspace = workspace;
        _settings = settings;
        _clock = clock;
        _phased = phased;
        _liveExecutor = liveExecutor;
        _verifier = verifier;

        PreparePhaseCommand = new AsyncRelayCommand(PreparePhaseAsync, () => !Busy && Plan is not null);
        ApprovePhaseCommand = new AsyncRelayCommand(ApprovePhaseAsync, () => !Busy && _phaseExecution?.Status == PhaseStatus.Simulated);
        ExecutePhaseCommand = new AsyncRelayCommand(ExecutePhaseAsync, () => !Busy && Plan is not null && _phaseExecution?.Status == PhaseStatus.Approved);
        RollbackPhaseCommand = new AsyncRelayCommand(RollbackPhaseAsync, () => !Busy && _phaseExecution is not null);

        BuildPlanCommand = new AsyncRelayCommand(BuildPlanAsync, () => !Busy);
        SimulateCommand = new AsyncRelayCommand(SimulateAsync, () => !Busy && Plan is not null);
        SubmitCommand = new AsyncRelayCommand(SubmitAsync, () => !Busy && Plan is not null);
        ApproveCommand = new AsyncRelayCommand(() => DecideAsync(ApprovalDecision.Approve), () => !Busy && Approval is not null);
        RejectCommand = new AsyncRelayCommand(() => DecideAsync(ApprovalDecision.Reject), () => !Busy && Approval is not null);
        RequestChangesCommand = new AsyncRelayCommand(() => DecideAsync(ApprovalDecision.RequestChanges), () => !Busy && Approval is not null);
        ExportPackageCommand = new AsyncRelayCommand(ExportPackageAsync, () => !Busy && Plan is not null);
        ExportScriptCommand = new AsyncRelayCommand(() => ExportTextAsync("PowerShell script (*.ps1)|*.ps1", ".ps1", _workspace.GeneratePlanScript), () => !Busy && Plan is not null);
        ExportRollbackCommand = new AsyncRelayCommand(() => ExportTextAsync("PowerShell script (*.ps1)|*.ps1", ".ps1", _workspace.GenerateRollbackScript), () => !Busy && Plan is not null);
        ImportPackageCommand = new AsyncRelayCommand(ImportPackageAsync, () => !Busy);
        ExportObjectsCommand = new AsyncRelayCommand(ExportObjectsAsync);
        SaveIaCTemplatesCommand = new AsyncRelayCommand(SaveInfrastructureTemplatesAsync);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => !Busy && Plan is not null);
    }

    public override string Title => "Deployment";
    public override string Subtitle => "Review, simulate, approve and apply — with a rollback package for every plan.";

    public ObservableCollection<SelectableRecommendation> Candidates { get; } = new();

    public IReadOnlyList<DeploymentMode> Modes { get; } = Enum.GetValues<DeploymentMode>();
    public IReadOnlyList<DeploymentPhaseDefinition> PhaseDefinitions => DeploymentPhases.All;
    public IReadOnlyList<string> ApproverRoles => SeparationOfDuties.RecommendedApproverRoles;
    public bool LiveExecutionEnabled
    {
        get => _liveExecutor.LiveExecutionEnabled;
        set
        {
            _liveExecutor.SetLiveExecutionEnabled(value);
            Raise(nameof(LiveExecutionEnabled));
        }
    }

    public DeploymentPhaseDefinition SelectedPhase
    {
        get => _selectedPhase;
        set => SetProperty(ref _selectedPhase, value);
    }

    public PhaseExecution? PhaseExecution { get => _phaseExecution; private set => SetProperty(ref _phaseExecution, value); }
    public string PhaseApprover { get => _phaseApprover; set => SetProperty(ref _phaseApprover, value); }
    public string PhaseApproverRole { get => _phaseApproverRole; set => SetProperty(ref _phaseApproverRole, value); }
    public bool PhaseRiskAccepted { get => _phaseRiskAccepted; set => SetProperty(ref _phaseRiskAccepted, value); }
    public string PhaseStatusText { get => _phaseStatusText; private set => SetProperty(ref _phaseStatusText, value); }
    public string PhaseLog { get => _phaseLog; private set => SetProperty(ref _phaseLog, value); }

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

    public ICommand PreparePhaseCommand { get; }
    public ICommand ApprovePhaseCommand { get; }
    public ICommand ExecutePhaseCommand { get; }
    public ICommand RollbackPhaseCommand { get; }
    public ICommand BuildPlanCommand { get; }
    public ICommand SimulateCommand { get; }
    public ICommand SubmitCommand { get; }
    public ICommand ApproveCommand { get; }
    public ICommand RejectCommand { get; }
    public ICommand RequestChangesCommand { get; }
    public ICommand ExportPackageCommand { get; }
    public ICommand ExportScriptCommand { get; }
    public ICommand ExportRollbackCommand { get; }
    public ICommand ImportPackageCommand { get; }
    public ICommand ExportObjectsCommand { get; }
    public ICommand SaveIaCTemplatesCommand { get; }
    public ICommand ApplyCommand { get; }

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

    private async Task PreparePhaseAsync()
    {
        if (Plan is null) return;
        var tenant = _workspace.Tenant;
        if (tenant is null)
        {
            PhaseStatusText = "Run an assessment first so the phase has a snapshot and backup source.";
            return;
        }

        Busy = true;
        try
        {
            var execution = _phased.Prepare(Plan, SelectedPhase.Phase, Actor);
            var (updated, result) = await _phased.SimulateAndBackupAsync(Plan, execution, tenant, Actor).ConfigureAwait(true);

            PhaseExecution = updated;
            _phaseSimulated = result.Succeeded;
            PhaseLog = string.Join(Environment.NewLine, updated.Log);
            PhaseStatusText = result.Summary;
        }
        catch (Exception ex)
        {
            PhaseStatusText = $"Phase preparation failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
            RefreshPhaseCommands();
        }
    }

    private async Task ApprovePhaseAsync()
    {
        if (_phaseExecution is null) return;

        Busy = true;
        try
        {
            PhaseExecution = await _phased.ApproveAsync(_phaseExecution, Actor, PhaseApprover, PhaseApproverRole).ConfigureAwait(true);
            PhaseLog = string.Join(Environment.NewLine, PhaseExecution.Log);
            PhaseStatusText = $"Phase approved by {PhaseApprover} ({PhaseApproverRole}).";
        }
        catch (Exception ex)
        {
            PhaseStatusText = $"Approval failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
            RefreshPhaseCommands();
        }
    }

    private async Task ExecutePhaseAsync()
    {
        if (Plan is null || _phaseExecution is null) return;

        var tenant = _workspace.Tenant;
        if (tenant is null)
        {
            PhaseStatusText = "No tenant snapshot is available.";
            return;
        }

        Busy = true;
        try
        {
            var settings = await _settings.LoadAsync().ConfigureAwait(true);
            var (verified, message) = await _verifier.VerifyAsync(settings.Auth).ConfigureAwait(true);

            var authorization = new DeploymentAuthorization(
                _phaseSimulated,
                _phaseExecution.Status == PhaseStatus.Approved,
                true,
                true,
                verified,
                verified,
                PhaseRiskAccepted || Plan.DestructiveOperationCount == 0);

            var result = await _liveExecutor.ExecuteAsync(Plan, _phaseExecution, settings.Auth, tenant, authorization)
                .ConfigureAwait(true);

            PhaseExecution = await _phased.RecordExecutionAsync(_phaseExecution, result, Actor).ConfigureAwait(true);

            if (result.Succeeded)
            {
                var before = tenant;
                await _workspace.AssessAsync(settings.Auth, settings.DefaultMode).ConfigureAwait(true);
                var report = DeploymentValidation.Validate(Plan, before, _workspace.Tenant!);
                PhaseExecution = await _phased.RecordValidationAsync(PhaseExecution, report, Actor).ConfigureAwait(true);
                PhaseStatusText = $"{result.Summary} {report.Summary}";
            }
            else
            {
                PhaseStatusText = $"{result.Summary} Authentication: {message}";
            }

            PhaseLog = string.Join(Environment.NewLine, PhaseExecution.Log);
        }
        catch (Exception ex)
        {
            PhaseStatusText = $"Execution failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
            RefreshPhaseCommands();
        }
    }

    private async Task RollbackPhaseAsync()
    {
        if (_phaseExecution is null) return;

        Busy = true;
        try
        {
            var backup = await _workspace.GetLatestBackupAsync().ConfigureAwait(true);
            if (backup is null)
            {
                PhaseStatusText = "No backup is available; rollback is blocked.";
                return;
            }

            PhaseExecution = _phased.MarkRolledBack(_phaseExecution, backup);
            PhaseLog = string.Join(Environment.NewLine, PhaseExecution.Log);
            PhaseStatusText = "Phase marked rolled back. Export and run the rollback script, then re-assess to confirm the restored state.";
        }
        catch (Exception ex)
        {
            PhaseStatusText = $"Rollback failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
            RefreshPhaseCommands();
        }
    }

    private void RefreshPhaseCommands()
    {
        foreach (var command in new[] { PreparePhaseCommand, ApprovePhaseCommand, ExecutePhaseCommand, RollbackPhaseCommand })
        {
            (command as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
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
            var plan = await _workspace.CreatePlanAsync(selected, Mode, Actor).ConfigureAwait(true);
            SetPlan(plan);
            Status = $"Change plan {plan.Id:N} created with {plan.Operations.Count} operation(s) and a pre-change backup.";
        }
        catch (Exception ex)
        {
            Status = $"Plan creation failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
            RefreshPlanCommands();
        }
    }

    private void SetPlan(ChangePlan plan)
    {
        Plan = plan;

        var impact = plan.Impact;
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
        ApprovalState = "Plan ready. Simulation and approval are required before apply.";
    }

    private void RefreshPlanCommands()
    {
        foreach (var command in new[] { SimulateCommand, SubmitCommand, ExportPackageCommand, ExportScriptCommand, ExportRollbackCommand, ApplyCommand })
        {
            (command as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        RefreshPhaseCommands();
    }

    private async Task ExportPackageAsync()
    {
        if (Plan is null) return;

        var dialog = new SaveFileDialog
        {
            Filter = "Configuration package (*.json)|*.json",
            FileName = $"pgc-plan-{Plan.Id:N}.json"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            await File.WriteAllTextAsync(dialog.FileName, _workspace.ExportPlanJson(Plan)).ConfigureAwait(true);
            Status = $"Configuration package exported to {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            Status = $"Export failed: {ex.Message}";
        }
    }

    private async Task ExportTextAsync(string filter, string extension, Func<ChangePlan, string> render)
    {
        if (Plan is null) return;

        var dialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = $"pgc-plan-{Plan.Id:N}{extension}"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            await File.WriteAllTextAsync(dialog.FileName, render(Plan)).ConfigureAwait(true);
            Status = $"Exported to {dialog.FileName}. Review it before execution.";
        }
        catch (Exception ex)
        {
            Status = $"Export failed: {ex.Message}";
        }
    }

    private async Task ExportObjectsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PowerShell script (*.ps1)|*.ps1",
            FileName = "purview-export-existing.ps1"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            await File.WriteAllTextAsync(dialog.FileName, ConfigurationExportScript.Script).ConfigureAwait(true);
            Status = $"Saved {dialog.FileName}. It only reads existing labels, DLP and retention objects into JSON.";
        }
        catch (Exception ex)
        {
            Status = $"Export script failed: {ex.Message}";
        }
    }

    private async Task SaveInfrastructureTemplatesAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "ARM template (*.json)|*.json",
            FileName = "purview-infrastructure.json"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var directory = Path.GetDirectoryName(dialog.FileName) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(dialog.FileName);
            var armPath = Path.Combine(directory, baseName + ".json");
            var bicepPath = Path.Combine(directory, baseName + ".bicep");
            var terraformPath = Path.Combine(directory, baseName + ".tf");

            await File.WriteAllTextAsync(armPath, InfrastructureTemplates.Arm).ConfigureAwait(true);
            await File.WriteAllTextAsync(bicepPath, InfrastructureTemplates.Bicep).ConfigureAwait(true);
            await File.WriteAllTextAsync(terraformPath, InfrastructureTemplates.Terraform).ConfigureAwait(true);

            Status = $"Saved {armPath}, {bicepPath} and {terraformPath}. They cover the Purview account only; compliance objects use the JSON/PowerShell export.";
        }
        catch (Exception ex)
        {
            Status = $"Template export failed: {ex.Message}";
        }
    }

    private async Task ImportPackageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Configuration package (*.json)|*.json|All files|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        Busy = true;
        try
        {
            var json = await File.ReadAllTextAsync(dialog.FileName).ConfigureAwait(true);
            SetPlan(_workspace.ImportPlan(json));
            Status = $"Imported plan with {Plan!.Operations.Count} operation(s). Simulation and approval are required again.";
        }
        catch (Exception ex)
        {
            Status = $"Import failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
            RefreshPlanCommands();
        }
    }

    private async Task ApplyAsync()
    {
        if (Plan is null) return;

        Busy = true;
        try
        {
            var result = await _workspace.ApplyAsync(Plan).ConfigureAwait(true);
            SimulationLog = string.Join(Environment.NewLine, result.Log);
            Status = result.Summary;
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
