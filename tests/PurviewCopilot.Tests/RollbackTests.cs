using System.Text.Json;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Domain;
using PurviewCopilot.Infrastructure.Deployment;
using Xunit;

namespace PurviewCopilot.Tests;

public class RollbackTests
{
    private const string Tenant = "contoso.onmicrosoft.com";

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class MemoryBackups : IBackupStore
    {
        public List<ConfigurationBackup> Items { get; } = new();

        public Task SaveAsync(ConfigurationBackup backup, CancellationToken cancellationToken = default)
        {
            Items.Add(backup);
            return Task.CompletedTask;
        }

        public Task<ConfigurationBackup?> GetLatestAsync(string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.LastOrDefault(b => b.TenantId == tenantId));

        public Task<IReadOnlyList<ConfigurationBackup>> ListAsync(string tenantId, int take = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConfigurationBackup>>(Items.Where(b => b.TenantId == tenantId).ToArray());
    }

    private static ChangeOperation Operation(string id, OperationAction action, Reversibility reversibility = Reversibility.Restorable) =>
        new(id, ModuleKey.SensitivityLabels, action, $"Purview/Labels/{id}", "{\"label\":\"Confidential\"}", false, true, reversibility);

    private static ChangePlan Plan(params ChangeOperation[] operations) =>
        new(Guid.NewGuid(), Tenant, DeploymentMode.GuidedDeployment, "Plan", Array.Empty<string>(), operations,
            new ChangeImpact("", "", "", "", "", "", "", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
            DateTimeOffset.UtcNow, "alice", true);

    private static async Task<ConfigurationBackup> PhaseBackupAsync(ChangePlan plan, params ChangeOperation[] operations)
    {
        var store = new MemoryBackups();
        var service = new BackupService(store, new Clock());
        return await service.CapturePhaseAsync(
            new TenantSnapshot { TenantId = plan.TenantId }, plan,
            DeploymentPhases.For(DeploymentPhase.InformationProtection), operations, "alice");
    }

    [Fact]
    public async Task PhaseBackupProducesAReadyRestorePlan()
    {
        var operation = Operation("lbl-1", OperationAction.Update);
        var plan = Plan(operation);
        var backup = await PhaseBackupAsync(plan, operation);

        var restore = RollbackPlanner.Build(backup, plan);

        Assert.True(restore.Ready, restore.Summary);
        var step = Assert.Single(restore.Steps);
        Assert.Contains("Restore", step.Instruction, StringComparison.Ordinal);
        Assert.Contains("Connect-IPPSSession", RollbackPlanner.BuildScript(plan, restore), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackupForAnotherTenantIsRejected()
    {
        var operation = Operation("lbl-1", OperationAction.Update);
        var plan = Plan(operation);
        var backup = await PhaseBackupAsync(plan, operation);

        var restore = RollbackPlanner.Build(backup with { TenantId = "fabrikam.onmicrosoft.com" }, plan);

        Assert.False(restore.Ready);
        Assert.Contains(restore.Checks, c => !c.Passed && c.Name.Contains("tenant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IrreversibleWorkBlocksTheRestore()
    {
        var operation = Operation("drop", OperationAction.Delete, Reversibility.Irreversible);
        var plan = Plan(operation);
        var backup = await PhaseBackupAsync(plan, operation);

        var restore = RollbackPlanner.Build(backup, plan);

        Assert.False(restore.Ready);
        Assert.Contains(restore.Checks, c => !c.Passed && c.Detail.Contains("drop", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeletionWithoutAnObjectExportIsReportedAsUnready()
    {
        var operation = Operation("removed", OperationAction.Delete, Reversibility.Restorable);
        var plan = Plan(operation);
        var backup = await PhaseBackupAsync(plan, operation);

        var restore = RollbackPlanner.Build(backup, plan);

        Assert.False(restore.Ready);
        Assert.Contains(restore.Steps, s => s.Instruction.Contains("object export", StringComparison.Ordinal));
        Assert.Contains(restore.Checks, c => !c.Passed && c.Name.Contains("Deleted objects", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedPayloadIsNotReady()
    {
        var backup = new ConfigurationBackup(Guid.NewGuid(), Tenant, "pre-phase/x", "1.0", "{not json", DateTimeOffset.UtcNow, "alice");

        var restore = RollbackPlanner.Build(backup, Plan(Operation("lbl-1", OperationAction.Update)));

        Assert.False(restore.Ready);
        Assert.Empty(restore.Steps);
    }

    [Fact]
    public async Task OperationMissingFromTheBackupBlocksTheRestore()
    {
        var backedUp = Operation("lbl-1", OperationAction.Update);
        var plan = Plan(backedUp, Operation("lbl-2", OperationAction.Update));
        var backup = await PhaseBackupAsync(plan, backedUp);

        var restore = RollbackPlanner.Build(backup, plan);

        Assert.False(restore.Ready);
        Assert.Contains(restore.Checks, c => !c.Passed && c.Detail.Contains("lbl-2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecutorFailsClosedUnlessEnabledAndCertificateIsPresent()
    {
        var operation = Operation("lbl-1", OperationAction.Update);
        var plan = Plan(operation);
        var backup = await PhaseBackupAsync(plan, operation);
        var runner = new FakeRunner();
        var tenant = new TenantSnapshot { TenantId = Tenant, TenantDomain = Tenant };
        var auth = new AuthConfiguration(AuthMode.Certificate, "tenant", "client", "THUMB", null, null);

        var disabled = await new PowerShellRollbackExecutor(runner).RestoreAsync(backup, plan, auth, tenant);
        Assert.False(disabled.Succeeded);
        Assert.Equal(0, runner.Calls);

        var secret = new AuthConfiguration(AuthMode.ClientSecret, "tenant", "client", null, "secret", null);
        var enabled = new PowerShellRollbackExecutor(runner, allowLiveExecution: true);
        var blocked = await enabled.RestoreAsync(backup, plan, secret, tenant);
        Assert.False(blocked.Succeeded);
        Assert.Equal(0, runner.Calls);

        var ok = await enabled.RestoreAsync(backup, plan, auth, tenant);
        Assert.True(ok.Succeeded);
        Assert.Equal(1, runner.Calls);
        Assert.Contains("Connect-IPPSSession", runner.Script, StringComparison.Ordinal);
        Assert.Equal(Tenant, runner.Environment["PGC_ORG"]);
    }

    [Fact]
    public async Task ExecutorRefusesABackupFromAnotherTenant()
    {
        var operation = Operation("lbl-1", OperationAction.Update);
        var plan = Plan(operation);
        var backup = await PhaseBackupAsync(plan, operation);
        var runner = new FakeRunner();
        var auth = new AuthConfiguration(AuthMode.Certificate, "tenant", "client", "THUMB", null, null);
        var executor = new PowerShellRollbackExecutor(runner, allowLiveExecution: true);

        var result = await executor.RestoreAsync(
            backup, plan, auth, new TenantSnapshot { TenantId = "fabrikam.onmicrosoft.com" });

        Assert.False(result.Succeeded);
        Assert.Equal(0, runner.Calls);
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
