using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Workspace;

/// <summary>One row of the cross-tenant portfolio, built from the newest assessment per tenant.</summary>
public sealed record PortfolioRow(
    string TenantId,
    string DisplayName,
    bool Assessed,
    double Overall,
    double Security,
    double Compliance,
    double Risk,
    MaturityLevel Maturity,
    DateTimeOffset? AssessedUtc)
{
    public string OverallText => Assessed ? $"{Overall:0.#}" : "—";
    public string SecurityText => Assessed ? $"{Security:0.#}" : "—";
    public string ComplianceText => Assessed ? $"{Compliance:0.#}" : "—";
    public string RiskText => Assessed ? $"{Risk:0.#}" : "—";
    public string MaturityText => Assessed ? Maturity.ToString() : "Not assessed";
    public string AssessedText => AssessedUtc is null ? "Never" : AssessedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

/// <summary>
/// Reads each customer tenant's latest stored assessment. Nothing crosses tenants except the
/// aggregate row: a profile never sees another profile's findings through this view.
/// </summary>
public static class TenantPortfolio
{
    public static async Task<IReadOnlyList<PortfolioRow>> BuildAsync(
        IReadOnlyList<TenantProfile> profiles,
        IAssessmentStore store,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<PortfolioRow>();

        foreach (var profile in profiles.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var latest = await store.GetLatestAsync(profile.TenantId, cancellationToken).ConfigureAwait(false);

            if (latest is null)
            {
                rows.Add(new PortfolioRow(profile.TenantId, profile.DisplayName, false, 0, 0, 0, 0,
                    MaturityLevel.Initial, null));
                continue;
            }

            rows.Add(new PortfolioRow(
                profile.TenantId,
                profile.DisplayName,
                true,
                latest.Scores.Overall,
                latest.Scores.Security,
                latest.Scores.Compliance,
                latest.Scores.Risk,
                latest.Scores.MaturityLevel,
                latest.RunUtc));
        }

        return rows;
    }
}
