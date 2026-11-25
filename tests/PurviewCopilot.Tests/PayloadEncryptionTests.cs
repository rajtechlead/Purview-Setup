using Microsoft.Data.Sqlite;
using PurviewCopilot.Application.Ai;
using PurviewCopilot.Domain;
using PurviewCopilot.Infrastructure.Data;
using Xunit;

namespace PurviewCopilot.Tests;

public class PayloadEncryptionTests
{
    [Fact]
    public async Task AssessmentEvidence_IsEncryptedAtRest_AndStillLoads()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pgc-enc-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var store = new SqliteStore(directory);

            const string marker = "Contoso-Secret-Evidence-Marker";
            var finding = new Finding("DLP-01", ModuleKey.DataLossPrevention, marker, FindingStatus.Fail, Severity.High,
                "evidence", "remediation", Array.Empty<string>());

            var result = new AssessmentResult(
                Guid.NewGuid(), "tenant-enc", DateTimeOffset.UtcNow, "AssessmentOnly",
                new ScoreSet(70, 70, 70, 70, MaturityLevel.Defined, 0, 70, 70, 70, Array.Empty<ModuleScore>()),
                new[] { finding }, Array.Empty<Recommendation>(), 49);

            await store.SaveAsync(result);

            using var connection = new SqliteConnection($"Data Source={store.DatabasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload FROM assessments WHERE tenant_id='tenant-enc'";
            var raw = (string)command.ExecuteScalar()!;

            Assert.DoesNotContain(marker, raw, StringComparison.Ordinal);
            Assert.StartsWith("dpapi:v1:", raw, StringComparison.Ordinal);

            var loaded = await ((PurviewCopilot.Application.Abstractions.IAssessmentStore)store)
                .GetLatestAsync("tenant-enc");

            Assert.NotNull(loaded);
            Assert.Equal("tenant-enc", loaded!.TenantId);
            Assert.Equal(marker, Assert.Single(loaded.Findings).Title);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AiUsageSummary_AggregatesTokensCostsAndFailures()
    {
        var at = new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
        var records = new[]
        {
            new AiUsageRecord(Guid.NewGuid(), "openai", "gpt", 100, 50, 0.01, true, null, at),
            new AiUsageRecord(Guid.NewGuid(), "anthropic", "claude", 200, 100, 0.02, false, "timeout", at)
        };

        var summary = AiUsageSummary.From(records);

        Assert.Equal(2, summary.Requests);
        Assert.Equal(1, summary.FailedRequests);
        Assert.Equal(300, summary.PromptTokens);
        Assert.Equal(150, summary.CompletionTokens);
        Assert.Equal(450, summary.TotalTokens);
        Assert.Equal(0.03, summary.CostUsd, 3);
        Assert.Equal(2, summary.Providers);
        Assert.Equal(50, summary.SuccessRatePercent);
    }

    [Fact]
    public void AiUsageSummary_EmptyLedger_IsZeroed()
    {
        var summary = AiUsageSummary.From(Array.Empty<AiUsageRecord>());

        Assert.Equal(AiUsageSummary.Empty, summary);
        Assert.Equal(0, summary.SuccessRatePercent);
    }
}
