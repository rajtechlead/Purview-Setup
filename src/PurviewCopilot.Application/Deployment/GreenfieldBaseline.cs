using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

/// <summary>
/// First-run path for a tenant with no Purview configuration. Greenfield plans start from the
/// foundational controls instead of whatever happens to score worst, and the empty tenant is
/// detected from collected evidence rather than assumed from the selected mode.
/// </summary>
public static class GreenfieldBaseline
{
    public static readonly IReadOnlySet<string> FoundationRecommendations =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SEN-01", "IPT-01", "IPT-02", "AUT-01", "DLP-01", "DLM-01", "AUD-02"
        };

    public static bool IsGreenfield(TenantSnapshot snapshot) =>
        snapshot.SensitivityLabelCount == 0 &&
        snapshot.LabelPolicyCount == 0 &&
        snapshot.AutoLabelingRuleCount == 0 &&
        snapshot.DlpPolicyCount == 0 &&
        snapshot.RetentionPolicyCount == 0 &&
        snapshot.RetentionLabelCount == 0;

    public static bool IsFoundation(string recommendationId) =>
        FoundationRecommendations.Contains(recommendationId);
}
