using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Ai;
using PurviewCopilot.Application.Reporting;
using PurviewCopilot.Domain;
using PurviewCopilot.Domain.Scoring;
using PurviewCopilot.Infrastructure.Data;
using Xunit;

namespace PurviewCopilot.Tests;

public class AiAndPersistenceTests
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

    private sealed class ScriptedProvider : IAiProvider
    {
        private readonly AiProviderException? _failure;

        public ScriptedProvider(string id, AiProviderException? failure = null)
        {
            Id = id;
            _failure = failure;
        }

        public string Id { get; }
        public int Calls { get; private set; }

        public Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (_failure is not null) throw _failure;
            return Task.FromResult(new AiResponse("answer", Id, "model", 100, 50, 0));
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
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

    private static AiProviderDescriptor Descriptor(string id, int priority, bool local = false, bool enabled = true) =>
        new(id, id, AiProviderKind.OpenAi, "https://example.invalid/v1", "model", "KEY", local, enabled, priority, 0.001, 0.002, 8000);

    [Fact]
    public async Task Failover_MovesToNextProvider_OnQuotaFailure()
    {
        var quota = new ScriptedProvider("p1", new AiProviderException("p1", AiFailureKind.QuotaExhausted, "quota"));
        var healthy = new ScriptedProvider("p2");
        var usage = new InMemoryUsage();
        var orchestrator = new AiFailoverOrchestrator(new Factory(quota, healthy), usage, new FixedClock());

        var response = await orchestrator.CompleteAsync(
            new[] { Descriptor("p1", 1), Descriptor("p2", 2) },
            new AiRequest("system", "user"));

        Assert.Equal("p2", response.ProviderId);
        Assert.Equal(1, quota.Calls);
        Assert.Equal(1, healthy.Calls);
        Assert.Equal(2, usage.Records.Count);
        Assert.Contains(usage.Records, r => !r.Succeeded && r.ErrorCode == nameof(AiFailureKind.QuotaExhausted));
    }

    [Fact]
    public async Task Failover_AllProvidersFail_Throws()
    {
        var a = new ScriptedProvider("a", new AiProviderException("a", AiFailureKind.Unauthorized, "401"));
        var b = new ScriptedProvider("b", new AiProviderException("b", AiFailureKind.ServerError, "500"));
        var orchestrator = new AiFailoverOrchestrator(new Factory(a, b), new InMemoryUsage(), new FixedClock());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.CompleteAsync(new[] { Descriptor("a", 1), Descriptor("b", 2) }, new AiRequest("s", "u")));

        Assert.Contains("All AI providers failed", ex.Message);
    }

    [Fact]
    public async Task Failover_SkipsDisabledProviders()
    {
        var disabled = new ScriptedProvider("off");
        var enabled = new ScriptedProvider("on");
        var orchestrator = new AiFailoverOrchestrator(new Factory(disabled, enabled), new InMemoryUsage(), new FixedClock());

        var response = await orchestrator.CompleteAsync(
            new[] { Descriptor("off", 1, enabled: false), Descriptor("on", 2) }, new AiRequest("s", "u"));

        Assert.Equal("on", response.ProviderId);
        Assert.Equal(0, disabled.Calls);
    }

    [Fact]
    public async Task Failover_ComputesCostFromTokenRates()
    {
        var provider = new ScriptedProvider("p");
        var orchestrator = new AiFailoverOrchestrator(new Factory(provider), new InMemoryUsage(), new FixedClock());

        var response = await orchestrator.CompleteAsync(new[] { Descriptor("p", 1) }, new AiRequest("s", "u"));

        // 100 prompt tokens * 0.001/1k + 50 completion * 0.002/1k
        Assert.Equal(0.0002, response.EstimatedCostUsd, 6);
    }

    [Fact]
    public void Sqlite_RoundTripsAssessment_ApprovalAndSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pgc-tests-" + Guid.NewGuid().ToString("N"));
        var store = new SqliteStore(directory);
        var clock = new FixedClock();

        var findings = new[]
        {
            new Finding("AUD-01", ModuleKey.Audit, "Audit enabled", FindingStatus.Pass, Severity.Critical, "on", "none", Array.Empty<string>())
        };
        var assessment = new AssessmentResult(
            Guid.NewGuid(), "tenant-1", clock.UtcNow, "AssessmentOnly",
            ScoringEngine.Compute(findings), findings, Array.Empty<Recommendation>(), 48);

        store.SaveAsync(assessment).GetAwaiter().GetResult();
        var loaded = ((IAssessmentStore)store).GetLatestAsync("tenant-1").GetAwaiter().GetResult();

        Assert.NotNull(loaded);
        Assert.Equal(assessment.Id, loaded!.Id);
        Assert.Single(loaded.Findings);

        var approval = new ApprovalRequest(Guid.NewGuid(), Guid.NewGuid(), ApprovalState.PendingCab, 2,
            Array.Empty<ApprovalEvent>(), clock.UtcNow, clock.UtcNow);
        store.SaveAsync(approval).GetAwaiter().GetResult();
        Assert.Equal(ApprovalState.PendingCab, store.GetByPlanAsync(approval.PlanId).GetAwaiter().GetResult()!.State);

        var settings = AppSettings.CreateDefault();
        settings.OfflineMode = true;
        store.SaveAsync(settings).GetAwaiter().GetResult();
        Assert.True(store.LoadAsync().GetAwaiter().GetResult().OfflineMode);

        store.WriteAsync(new AuditEntry(Guid.NewGuid(), clock.UtcNow, "tester", "UnitTest", "db", "ok", null))
            .GetAwaiter().GetResult();
        Assert.Contains(store.ReadAsync().GetAwaiter().GetResult(), e => e.Action == "UnitTest");

        store.Dispose();
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void HtmlReport_EscapesOrganization_AndIncludesScores()
    {
        var snapshot = Application.Demo.DemoTenant.Create();
        var findings = new[]
        {
            new Finding("DLP-01", ModuleKey.DataLossPrevention, "DLP exists", FindingStatus.Fail, Severity.Critical,
                "No policies", "Create baseline policy", new[] { "PCI-DSS 3.4" })
        };
        var assessment = new AssessmentResult(Guid.NewGuid(), snapshot.TenantId, DateTimeOffset.UtcNow,
            "AssessmentOnly", ScoringEngine.Compute(findings), findings, Array.Empty<Recommendation>(), 48);

        var html = new HtmlReportExporter().Render(new ReportContext(
            snapshot, assessment, "<script>alert(1)</script>", "tester", DateTimeOffset.UtcNow,
            FrameworkMapper.Map(findings)));

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("DLP-01", html);
        Assert.Contains("PCI-DSS", html);
    }

    [Fact]
    public void CsvReport_QuotesFields()
    {
        var snapshot = Application.Demo.DemoTenant.Create();
        var findings = new[]
        {
            new Finding("AUD-01", ModuleKey.Audit, "Title, with comma", FindingStatus.Fail, Severity.High,
                "Evidence \"quoted\"", "Fix", Array.Empty<string>())
        };
        var assessment = new AssessmentResult(Guid.NewGuid(), snapshot.TenantId, DateTimeOffset.UtcNow,
            "AssessmentOnly", ScoringEngine.Compute(findings), findings, Array.Empty<Recommendation>(), 48);

        var csv = new CsvReportExporter().Render(new ReportContext(
            snapshot, assessment, "Org", "tester", DateTimeOffset.UtcNow, Array.Empty<FrameworkCoverage>()));

        Assert.Contains("\"Title, with comma\"", csv);
        Assert.Contains("\"\"quoted\"\"", csv);
    }
}
