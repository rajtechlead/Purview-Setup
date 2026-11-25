using Microsoft.Data.Sqlite;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Workspace;
using PurviewCopilot.Domain;
using PurviewCopilot.Infrastructure.Data;
using Xunit;

namespace PurviewCopilot.Tests;

public class TenantProfileTests
{
    private sealed class FakeAssessmentStore : IAssessmentStore
    {
        private readonly Dictionary<string, AssessmentResult> _latest = new();

        public void Set(AssessmentResult result) => _latest[result.TenantId] = result;

        public Task SaveAsync(AssessmentResult result, CancellationToken cancellationToken = default)
        {
            _latest[result.TenantId] = result;
            return Task.CompletedTask;
        }

        public Task<AssessmentResult?> GetLatestAsync(string tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_latest.TryGetValue(tenantId, out var result) ? result : null);

        public Task<IReadOnlyList<AssessmentResult>> ListAsync(string tenantId, int take = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AssessmentResult>>(
                _latest.TryGetValue(tenantId, out var result) ? new[] { result } : Array.Empty<AssessmentResult>());
    }

    private static AssessmentResult Result(string tenantId, double overall) =>
        new(Guid.NewGuid(), tenantId, new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero), "AssessmentOnly",
            new ScoreSet(overall, overall - 5, overall - 10, overall - 12, MaturityLevel.Managed, 20, overall, overall, overall,
                Array.Empty<ModuleScore>()),
            Array.Empty<Finding>(), Array.Empty<Recommendation>(), 49);

    [Fact]
    public async Task TenantProfiles_RoundTrip_WithProtectedSecrets()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pgc-tenants-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var store = new SqliteStore(directory);

            var auth = new AuthConfiguration(
                AuthMode.ClientSecret, "tenant-a", "client-a", null, "tenant-a-secret", null);

            await store.SaveAsync(new TenantProfile("tenant-a", "Contoso", auth, DateTimeOffset.UtcNow));

            var profiles = await store.ListAsync();

            Assert.Single(profiles);
            Assert.Equal("Contoso", profiles[0].DisplayName);
            Assert.Equal("tenant-a", profiles[0].Auth.TenantId);
            Assert.Equal("tenant-a-secret", profiles[0].Auth.ClientSecret);

            using var connection = new SqliteConnection($"Data Source={store.DatabasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload FROM tenant_profiles WHERE tenant_id='tenant-a'";
            var raw = (string)command.ExecuteScalar()!;

            Assert.DoesNotContain("tenant-a-secret", raw, StringComparison.Ordinal);
            Assert.Contains("dpapi:v1:", raw, StringComparison.Ordinal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Portfolio_UsesEachTenantsOwnLatestAssessment()
    {
        var store = new FakeAssessmentStore();
        store.Set(Result("tenant-a", 80));
        store.Set(Result("tenant-b", 55));

        var profiles = new[]
        {
            new TenantProfile("tenant-a", "Alpha", new AuthConfiguration(AuthMode.Certificate, "tenant-a", "client", "thumb", null, null), DateTimeOffset.UtcNow),
            new TenantProfile("tenant-b", "Beta", new AuthConfiguration(AuthMode.Certificate, "tenant-b", "client", "thumb", null, null), DateTimeOffset.UtcNow),
            new TenantProfile("tenant-c", "Gamma", new AuthConfiguration(AuthMode.Certificate, "tenant-c", "client", "thumb", null, null), DateTimeOffset.UtcNow)
        };

        var rows = await TenantPortfolio.BuildAsync(profiles, store);

        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, rows.Select(r => r.DisplayName));
        Assert.Equal(80, rows[0].Overall);
        Assert.Equal(55, rows[1].Overall);
        Assert.False(rows[2].Assessed);
        Assert.Equal("—", rows[2].OverallText);
        Assert.Equal("Never", rows[2].AssessedText);
        Assert.Equal("Not assessed", rows[2].MaturityText);
    }
}
