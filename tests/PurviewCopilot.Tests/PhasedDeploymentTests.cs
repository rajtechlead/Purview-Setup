using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Auth;
using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Domain;
using PurviewCopilot.Infrastructure.Deployment;
using Xunit;

namespace PurviewCopilot.Tests;

public class PhasedDeploymentTests
{
    [Fact]
    public void Phases_PartitionOperationsByModule()
    {
        var plan = Plan(
            Operation("a", ModuleKey.Audit),
            Operation("b", ModuleKey.SensitivityLabels),
            Operation("c", ModuleKey.DataLossPrevention),
            Operation("d", ModuleKey.CopilotGovernance));

        Assert.Empty(DeploymentPhases.OperationsFor(plan, DeploymentPhase.Discovery));
        Assert.Single(DeploymentPhases.OperationsFor(plan, DeploymentPhase.MonitoringAndAudit));
        Assert.Single(DeploymentPhases.OperationsFor(plan, DeploymentPhase.InformationProtection));
        Assert.Single(DeploymentPhases.OperationsFor(plan, DeploymentPhase.DataLossPrevention));
        Assert.Single(DeploymentPhases.OperationsFor(plan, DeploymentPhase.InsiderRiskAndAi));
    }

    [Fact]
    public void Gate_BlocksEveryMissingSafeguard()
    {
        var plan = Plan(Operation("a", ModuleKey.Audit));
        var execution = Execution(PhaseStatus.Approved);

        var blocked = DeploymentGate.Evaluate(plan, execution, new DeploymentAuthorization(
            false, false, false, false, false, false, false));

        Assert.True(blocked.Blocked);
        Assert.Contains(blocked.Reasons, r => r.Contains("Simulation", StringComparison.Ordinal));
        Assert.Contains(blocked.Reasons, r => r.Contains("approval", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(blocked.Reasons, r => r.Contains("backup", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(blocked.Reasons, r => r.Contains("rollback", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(blocked.Reasons, r => r.Contains("Authentication", StringComparison.Ordinal));
        Assert.Contains(blocked.Reasons, r => r.Contains("connectivity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Gate_AllowsAFullyEvidencedApprovedPhase()
    {
        var result = DeploymentGate.Evaluate(
            Plan(Operation("a", ModuleKey.Audit)),
            Execution(PhaseStatus.Approved),
            new DeploymentAuthorization(true, true, true, true, true, true, true));

        Assert.False(result.Blocked);
    }

    [Fact]
    public void DestructivePlan_RequiresRiskAcceptance()
    {
        var plan = Plan(new ChangeOperation("a", ModuleKey.DataLossPrevention, OperationAction.Delete, "policy", "{}", true, true));

        var result = DeploymentGate.Evaluate(plan, Execution(PhaseStatus.Approved),
            new DeploymentAuthorization(true, true, true, true, true, true, false));

        Assert.True(result.Blocked);
        Assert.Contains(result.Reasons, r => r.Contains("risk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Approval_RequiresSimulationAndASeparateApprover()
    {
        var service = Service(out _, out _);
        var plan = Plan(Operation("a", ModuleKey.Audit));
        var prepared = service.Prepare(plan, DeploymentPhase.MonitoringAndAudit, "alice");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(prepared, "alice", "bob", "Change Manager"));

        var simulated = prepared with { Status = PhaseStatus.Simulated };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(simulated, "alice", "alice", "Change Manager"));

        var approved = await service.ApproveAsync(simulated, "alice", "bob", "Change Manager");
        Assert.Equal(PhaseStatus.Approved, approved.Status);
        Assert.Equal("bob", approved.ApprovedBy);
    }

    [Fact]
    public async Task SimulateAndBackup_CapturesAPhaseBackup()
    {
        var service = Service(out var store, out var executor, simulationSucceeds: true);
        var plan = Plan(Operation("a", ModuleKey.Audit));
        var prepared = service.Prepare(plan, DeploymentPhase.MonitoringAndAudit, "alice");

        var (execution, result) = await service.SimulateAndBackupAsync(plan, prepared, new TenantSnapshot { TenantId = "t" }, "alice");

        Assert.True(result.Succeeded);
        Assert.Equal(PhaseStatus.Simulated, execution.Status);
        Assert.Single(store.Backups);
        Assert.Contains("pre-phase/MonitoringAndAudit", store.Backups[0].Label, StringComparison.Ordinal);
        Assert.Equal(1, executor.Simulations);
    }

    [Fact]
    public async Task FailedSimulation_DoesNotCaptureBackupAndBlocksThePhase()
    {
        var service = Service(out var store, out _, simulationSucceeds: false);
        var plan = Plan(Operation("a", ModuleKey.Audit));
        var prepared = service.Prepare(plan, DeploymentPhase.MonitoringAndAudit, "alice");

        var (execution, result) = await service.SimulateAndBackupAsync(plan, prepared, new TenantSnapshot { TenantId = "t" }, "alice");

        Assert.False(result.Succeeded);
        Assert.Equal(PhaseStatus.Failed, execution.Status);
        Assert.Empty(store.Backups);
    }

    [Fact]
    public void NextPhase_RequiresPreviousValidation()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PhasedDeploymentService.EnsurePreviousValidated(DeploymentPhase.InformationProtection,
                Execution(PhaseStatus.Executed)));

        PhasedDeploymentService.EnsurePreviousValidated(DeploymentPhase.InformationProtection,
            Execution(PhaseStatus.Validated));
    }

    [Fact]
    public void Validation_DetectsAReducedControlSet()
    {
        var plan = Plan(Operation("a", ModuleKey.SensitivityLabels));
        var before = new TenantSnapshot { TenantId = "t", SensitivityLabelCount = 5, LabelPolicyCount = 2 };
        var after = new TenantSnapshot { TenantId = "t", SensitivityLabelCount = 3, LabelPolicyCount = 2 };

        var report = DeploymentValidation.Validate(plan, before, after);

        Assert.False(report.Passed);
        Assert.Contains(report.Checks, c => c.Name.Contains("labels", StringComparison.OrdinalIgnoreCase) && !c.Passed);
    }

    [Fact]
    public async Task LiveExecutor_FailsClosedUnlessEnabledAndFullyAuthorized()
    {
        var runner = new FakeRunner();
        var executor = new PowerShellDeploymentExecutor(runner, allowLiveExecution: false);
        var plan = Plan(Operation("a", ModuleKey.Audit));
        var auth = new AuthConfiguration(AuthMode.Certificate, "tenant", "client", "THUMB", null, null);
        var tenant = new TenantSnapshot { TenantId = "tenant", TenantDomain = "contoso.onmicrosoft.com" };

        var blocked = await executor.ExecuteAsync(plan, Execution(PhaseStatus.Approved), auth, tenant, Authorization());

        Assert.False(blocked.Succeeded);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task LiveExecutor_RunsThePhaseScriptWithCertificateEnvironmentWhenAuthorized()
    {
        var runner = new FakeRunner();
        var executor = new PowerShellDeploymentExecutor(runner, allowLiveExecution: true);
        var plan = Plan(Operation("a", ModuleKey.Audit));
        var auth = new AuthConfiguration(AuthMode.Certificate, "tenant", "client", "THUMB", null, null);
        var tenant = new TenantSnapshot { TenantId = "tenant", TenantDomain = "contoso.onmicrosoft.com" };

        var result = await executor.ExecuteAsync(plan, Execution(PhaseStatus.Approved), auth, tenant, Authorization());

        Assert.True(result.Succeeded);
        Assert.Equal(1, runner.Calls);
        Assert.Equal("client", runner.Environment["PGC_APP_ID"]);
        Assert.Equal("THUMB", runner.Environment["PGC_THUMBPRINT"]);
        Assert.Contains("Connect-IPPSSession", runner.Script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveExecutor_RejectsClientSecretAuthentication()
    {
        var runner = new FakeRunner();
        var executor = new PowerShellDeploymentExecutor(runner, allowLiveExecution: true);
        var auth = new AuthConfiguration(AuthMode.ClientSecret, "tenant", "client", null, "secret", null);

        var result = await executor.ExecuteAsync(
            Plan(Operation("a", ModuleKey.Audit)), Execution(PhaseStatus.Approved), auth,
            new TenantSnapshot { TenantId = "tenant" }, Authorization());

        Assert.False(result.Succeeded);
        Assert.Equal(0, runner.Calls);
    }

    private static PhasedDeploymentService Service(
        out FakeBackupStore store,
        out FakeExecutor executor,
        bool simulationSucceeds = true)
    {
        store = new FakeBackupStore();
        executor = new FakeExecutor(simulationSucceeds);
        return new PhasedDeploymentService(executor, new BackupService(store, new Clock()), new FakeAudit(), new Clock());
    }

    private static PhaseExecution Execution(PhaseStatus status) =>
        new(Guid.NewGuid(), Guid.NewGuid(), DeploymentPhase.MonitoringAndAudit, status, "bob", "Change Manager",
            DateTimeOffset.UtcNow, Array.Empty<string>());

    private static DeploymentAuthorization Authorization() =>
        new(true, true, true, true, true, true, true);

    private static ChangeOperation Operation(string id, ModuleKey module) =>
        new(id, module, OperationAction.Update, id, "{}", false, true);

    private static ChangePlan Plan(params ChangeOperation[] operations) =>
        new(Guid.NewGuid(), "tenant", DeploymentMode.GuidedDeployment, "Plan", Array.Empty<string>(),
            operations,
            new ChangeImpact("", "", "", "", "", "", "", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
            DateTimeOffset.UtcNow, "alice", true);

    private sealed class FakeExecutor : IChangeExecutor
    {
        private readonly bool _simulationSucceeds;
        public int Simulations { get; private set; }

        public FakeExecutor(bool simulationSucceeds) => _simulationSucceeds = simulationSucceeds;

        public Task<ExecutionResult> SimulateAsync(ChangePlan plan, CancellationToken cancellationToken = default)
        {
            Simulations++;
            return Task.FromResult(new ExecutionResult(_simulationSucceeds,
                _simulationSucceeds ? "ok" : "failed", new[] { "simulated" }));
        }

        public Task<ExecutionResult> ApplyAsync(ChangePlan plan, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ExecutionResult> RollbackAsync(ConfigurationBackup backup, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public string GenerateScript(ChangePlan plan) => string.Empty;
        public string GenerateRollbackScript(ChangePlan plan) => string.Empty;
    }

    private sealed class FakeBackupStore : IBackupStore
    {
        public List<ConfigurationBackup> Backups { get; } = new();

        public Task SaveAsync(ConfigurationBackup backup, CancellationToken cancellationToken = default)
        {
            Backups.Add(backup);
            return Task.CompletedTask;
        }

        public Task<ConfigurationBackup?> GetLatestAsync(string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Backups.LastOrDefault());

        public Task<IReadOnlyList<ConfigurationBackup>> ListAsync(string tenantId, int take = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConfigurationBackup>>(Backups);
    }

    private sealed class FakeAudit : IAuditLog
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditEntry>> ReadAsync(int take = 200, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditEntry>>(Array.Empty<AuditEntry>());
        public Task<AuditChainStatus> VerifyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuditChainStatus(true, 0, null, "ok"));
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeRunner : IPowerShellRunner
    {
        public int Calls { get; private set; }
        public string Script { get; private set; } = string.Empty;
        public IReadOnlyDictionary<string, string> Environment { get; private set; } = new Dictionary<string, string>();

        public Task<PowerShellResult> RunAsync(string script, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken = default)
        {
            Calls++;
            Script = script;
            Environment = environment;
            return Task.FromResult(new PowerShellResult(0, "done", string.Empty));
        }
    }
}
