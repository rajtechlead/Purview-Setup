using Microsoft.Data.Sqlite;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Ai;
using PurviewCopilot.Domain;
using PurviewCopilot.Infrastructure.Data;
using Xunit;

namespace PurviewCopilot.Tests;

public class AuditChainAndHealthTests
{
    private sealed class InMemoryUsage : IAiUsageStore
    {
        public List<AiUsageRecord> Records { get; } = new();

        public Task RecordAsync(AiUsageRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AiUsageRecord>> ListAsync(string? providerId = null, int take = 500, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiUsageRecord>>(
                Records.Where(r => providerId is null || r.ProviderId == providerId).Take(take).ToList());

        public Task<AiUsageTotals> GetTotalsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiUsageTotals(
                Records.Count,
                Records.Count(r => !r.Succeeded),
                Records.Sum(r => r.PromptTokens),
                Records.Sum(r => r.CompletionTokens),
                Records.Sum(r => r.EstimatedCostUsd),
                Records.Select(r => r.ProviderId).Distinct(StringComparer.OrdinalIgnoreCase).Count()));
    }

    private sealed class FakeProvider : IAiProvider
    {
        private readonly AiProviderException? _failure;

        public FakeProvider(string id, bool healthy = true, AiProviderException? failure = null)
        {
            Id = id;
            Healthy = healthy;
            _failure = failure;
        }

        public string Id { get; }
        public bool Healthy { get; set; }
        public int Calls { get; private set; }
        public int HealthChecks { get; private set; }

        public Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (_failure is not null) throw _failure;
            return Task.FromResult(new AiResponse("answer", Id, "model", 100, 50, 0));
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            HealthChecks++;
            return Task.FromResult(Healthy);
        }
    }

    private sealed class Factory : IAiProviderFactory
    {
        private readonly Dictionary<string, IAiProvider> _providers;

        public Factory(params IAiProvider[] providers) => _providers = providers.ToDictionary(p => p.Id);

        public IAiProvider Create(AiProviderDescriptor descriptor) => _providers[descriptor.Id];
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);
    }

    private static AiProviderDescriptor Descriptor(string id, int priority) =>
        new(id, id, AiProviderKind.OpenAi, "https://example.invalid/v1", "model", "KEY", false, true, priority, 0.001, 0.002, 8000);

    [Fact]
    public async Task AuditChain_VerifiesInOrder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pgc-audit-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var store = new SqliteStore(directory);

            var empty = await store.VerifyAsync();
            Assert.True(empty.IsValid);
            Assert.Equal(0, empty.Checked);

            for (var i = 0; i < 3; i++)
            {
                await store.WriteAsync(new AuditEntry(
                    Guid.NewGuid(), new DateTimeOffset(2026, 11, 25, 10, 0, i, TimeSpan.Zero),
                    "tester", "assess", "tenant", $"detail {i}", null));
            }

            var result = await store.VerifyAsync();

            Assert.True(result.IsValid);
            Assert.Equal(3, result.Checked);
            Assert.Null(result.FirstInvalidId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AuditChain_DetectsTampering()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pgc-audit-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var store = new SqliteStore(directory);

            for (var i = 0; i < 3; i++)
            {
                await store.WriteAsync(new AuditEntry(
                    Guid.NewGuid(), new DateTimeOffset(2026, 11, 25, 10, 0, i, TimeSpan.Zero),
                    "tester", "assess", "tenant", $"detail {i}", null));
            }

            using (var connection = new SqliteConnection($"Data Source={store.DatabasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE audit_log SET detail='tampered' WHERE detail='detail 1'";
                Assert.Equal(1, command.ExecuteNonQuery());
            }

            var result = await store.VerifyAsync();

            Assert.False(result.IsValid);
            Assert.NotNull(result.FirstInvalidId);
            Assert.Contains("chain broken", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Settings_ProtectsClientSecretAtRest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pgc-settings-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var store = new SqliteStore(directory);

            var settings = await store.LoadAsync();
            settings.Auth = settings.Auth with
            {
                Mode = AuthMode.ClientSecret,
                TenantId = "tenant",
                ClientId = "client",
                ClientSecret = "super-secret-value"
            };

            await store.SaveAsync(settings);

            using (var connection = new SqliteConnection($"Data Source={store.DatabasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT value FROM settings WHERE key='app'";
                var raw = (string)command.ExecuteScalar()!;

                Assert.DoesNotContain("super-secret-value", raw, StringComparison.Ordinal);
                Assert.Contains("dpapi:v1:", raw, StringComparison.Ordinal);
            }

            var loaded = await store.LoadAsync();
            Assert.Equal("super-secret-value", loaded.Auth.ClientSecret);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Failover_SkipsUnhealthyProvider()
    {
        var unhealthy = new FakeProvider("p1", healthy: false);
        var healthy = new FakeProvider("p2");
        var usage = new InMemoryUsage();
        var orchestrator = new AiFailoverOrchestrator(new Factory(unhealthy, healthy), usage, new FixedClock());

        var response = await orchestrator.CompleteAsync(
            new[] { Descriptor("p1", 1), Descriptor("p2", 2) },
            new AiRequest("system", "user"));

        Assert.Equal("p2", response.ProviderId);
        Assert.Equal(0, unhealthy.Calls);
        Assert.Equal(1, unhealthy.HealthChecks);
        Assert.Contains(usage.Records, r => r.ProviderId == "p1" && r.ErrorCode == nameof(AiFailureKind.Unhealthy));
    }

    [Fact]
    public async Task Failover_CoolsDownFailedProvider_OnNextCall()
    {
        var flaky = new FakeProvider("p1", failure: new AiProviderException("p1", AiFailureKind.QuotaExhausted, "quota"));
        var stable = new FakeProvider("p2");
        var usage = new InMemoryUsage();
        var orchestrator = new AiFailoverOrchestrator(new Factory(flaky, stable), usage, new FixedClock());

        var providers = new[] { Descriptor("p1", 1), Descriptor("p2", 2) };

        await orchestrator.CompleteAsync(providers, new AiRequest("system", "user"));
        Assert.Equal(1, flaky.Calls);

        var second = await orchestrator.CompleteAsync(providers, new AiRequest("system", "user"));

        Assert.Equal("p2", second.ProviderId);
        Assert.Equal(1, flaky.Calls);
        Assert.Equal(1, flaky.HealthChecks);
    }
}
