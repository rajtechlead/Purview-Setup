using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Ai;

/// <summary>Aggregated AI ledger for the local workspace: spend, tokens and reliability.</summary>
public sealed record AiUsageSummary(
    int Requests,
    int FailedRequests,
    int PromptTokens,
    int CompletionTokens,
    double CostUsd,
    int Providers)
{
    public int TotalTokens => PromptTokens + CompletionTokens;

    public double SuccessRatePercent =>
        Requests == 0 ? 0 : Math.Round((Requests - FailedRequests) * 100.0 / Requests, 1);

    public static AiUsageSummary Empty { get; } = new(0, 0, 0, 0, 0, 0);

    public static AiUsageSummary From(AiUsageTotals totals) => new(
        totals.Requests,
        totals.FailedRequests,
        totals.PromptTokens,
        totals.CompletionTokens,
        Math.Round(totals.CostUsd, 6),
        totals.Providers);

    public static AiUsageSummary From(IEnumerable<AiUsageRecord> records)
    {
        var list = records as IReadOnlyCollection<AiUsageRecord> ?? records.ToList();

        if (list.Count == 0)
        {
            return Empty;
        }

        return From(new AiUsageTotals(
            list.Count,
            list.Count(r => !r.Succeeded),
            list.Sum(r => r.PromptTokens),
            list.Sum(r => r.CompletionTokens),
            list.Sum(r => r.EstimatedCostUsd),
            list.Select(r => r.ProviderId).Distinct(StringComparer.OrdinalIgnoreCase).Count()));
    }
}
