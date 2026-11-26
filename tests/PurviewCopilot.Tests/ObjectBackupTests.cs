using System.Text.RegularExpressions;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class ObjectBackupTests
{
    [Fact]
    public async Task ObjectBackup_RunsTheReadOnlyExport_AndStoresTheObjects()
    {
        var runner = new WritingRunner("""{"labels":[{"name":"Confidential"}],"dlpPolicies":[{"name":"PII"}]}""");
        var store = new Store();
        var service = new ObjectBackupService(runner, new BackupService(store, new Clock()), new Clock());
        var plan = Plan();

        var backup = await service.CaptureAsync(
            new TenantSnapshot { TenantId = "t", TenantDomain = "contoso.onmicrosoft.com" },
            plan, Auth(), "alice");

        Assert.Equal("objects/" + plan.Id.ToString("N"), backup.Label);
        Assert.Contains("\"objects\"", backup.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("Confidential", backup.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("Get-Label", runner.Script, StringComparison.Ordinal);
        Assert.Single(store.Backups);
    }

    [Fact]
    public async Task ObjectBackup_FailsWhenTheExportFails()
    {
        var runner = new WritingRunner(string.Empty, exitCode: 1);
        var service = new ObjectBackupService(runner, new BackupService(new Store(), new Clock()), new Clock());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureAsync(
            new TenantSnapshot { TenantId = "t", TenantDomain = "contoso" }, Plan(), Auth(), "alice"));
    }

    [Fact]
    public async Task ObjectBackup_RequiresCertificateAuthentication()
    {
        var service = new ObjectBackupService(new WritingRunner("{}"), new BackupService(new Store(), new Clock()), new Clock());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureAsync(
            new TenantSnapshot { TenantId = "t" }, Plan(),
            new AuthConfiguration(AuthMode.ClientSecret, "t", "c", null, "secret", null), "alice"));
    }

    private static ChangePlan Plan() =>
        new(Guid.NewGuid(), "t", DeploymentMode.GuidedDeployment, "Plan", Array.Empty<string>(),
            new[] { new ChangeOperation("a", ModuleKey.Audit, OperationAction.Update, "audit", "{}", false, true) },
            new ChangeImpact("", "", "", "", "", "", "", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
            DateTimeOffset.UtcNow, "alice", true);

    private static AuthConfiguration Auth() =>
        new(AuthMode.Certificate, "tenant", "client", "THUMB", null, null);

    private sealed class WritingRunner : IPowerShellRunner
    {
        private readonly string _json;
        private readonly int _exitCode;
        public string Script { get; private set; } = string.Empty;

        public WritingRunner(string json, int exitCode = 0)
        {
            _json = json;
            _exitCode = exitCode;
        }

        public Task<PowerShellResult> RunAsync(string script, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken = default)
        {
            Script = script;
            var match = Regex.Match(script, @"-OutputPath '([^']+)'");
            if (match.Success && _exitCode == 0)
            {
                File.WriteAllText(match.Groups[1].Value, _json);
            }

            return Task.FromResult(new PowerShellResult(_exitCode, string.Empty, _exitCode == 0 ? string.Empty : "export failed"));
        }
    }

    private sealed class Store : IBackupStore
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

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
