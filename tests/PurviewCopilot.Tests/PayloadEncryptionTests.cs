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
    public async Task UsageTotals_CoverEveryRowBeyondTheListingLimit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pgc-usage-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var store = new SqliteStore(directory);

            using (var connection = new SqliteConnection($"Data Source={store.DatabasePath}"))
            {
                connection.Open();
                using var transaction = connection.BeginTransaction();

                for (var i = 0; i < 1005; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = "INSERT INTO ai_usage(id, provider_id, model, prompt_tokens, completion_tokens, cost, succeeded, error_code, at_utc) VALUES ($id,$provider,$model,10,5,0.001,$ok,$error,$at)";
                    insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                    insert.Parameters.AddWithValue("$provider", i % 2 == 0 ? "openai" : "local");
                    insert.Parameters.AddWithValue("$model", "model");
                    insert.Parameters.AddWithValue("$ok", i % 3 != 0 ? 1 : 0);
                    insert.Parameters.AddWithValue("$error", i % 3 == 0 ? "timeout" : DBNull.Value);
                    insert.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.AddSeconds(i).ToString("O"));
                    insert.ExecuteNonQuery();
                }

                transaction.Commit();
            }

            var listed = await ((PurviewCopilot.Application.Abstractions.IAiUsageStore)store).ListAsync(null, 1000);
            Assert.Equal(1000, listed.Count);

            var totals = await ((PurviewCopilot.Application.Abstractions.IAiUsageStore)store).GetTotalsAsync();

            Assert.Equal(1005, totals.Requests);
            Assert.Equal(335, totals.FailedRequests);
            Assert.Equal(10050, totals.PromptTokens);
            Assert.Equal(5025, totals.CompletionTokens);
            Assert.Equal(1.005, totals.CostUsd, 3);
            Assert.Equal(2, totals.Providers);

            var summary = AiUsageSummary.From(totals);
            Assert.Equal(1005, summary.Requests);
            Assert.Equal(1.005, summary.CostUsd, 3);
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
