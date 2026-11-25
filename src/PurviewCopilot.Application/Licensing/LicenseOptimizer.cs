using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Licensing;

public sealed record LicenseInsight(
    string Id,
    string Title,
    string Detail,
    string Recommendation,
    Severity Severity,
    double MonthlyOpportunityUsd);

/// <summary>
/// Analyses entitled SKUs for unused capacity, prerequisite gaps and over-assignment.
/// Cost figures are indicative only (no price list is embedded); utilization is exact from SKU counts.
/// </summary>
public static class LicenseOptimizer
{
    public static IReadOnlyList<LicenseInsight> Analyze(TenantSnapshot snapshot)
    {
        var insights = new List<LicenseInsight>();

        if (snapshot.Licenses.Count == 0)
        {
            insights.Add(new LicenseInsight(
                "LIC-000", "No licence data collected",
                "SKU inventory is empty, so optimisation cannot be performed.",
                "Run discovery with Organization.Read.All consent or attach the admin-center licence report.",
                Severity.Medium, 0));
            return insights;
        }

        var unused = snapshot.Licenses.Where(l => l.Enabled - l.Consumed >= 10).ToList();
        foreach (var sku in unused)
        {
            insights.Add(new LicenseInsight(
                $"LIC-UNUSED-{sku.PartNumber}",
                $"Unused capacity in {sku.PartNumber}",
                $"{sku.Enabled - sku.Consumed} of {sku.Enabled} seats are unassigned.",
                "Reclaim or right-size the subscription at renewal; confirm no planned onboarding depends on the seats.",
                Severity.Low, 0));
        }

        var oversubscribed = snapshot.Licenses.Where(l => l.Consumed > l.Enabled).ToList();
        foreach (var sku in oversubscribed)
        {
            insights.Add(new LicenseInsight(
                $"LIC-OVER-{sku.PartNumber}",
                $"{sku.PartNumber} is over-assigned",
                $"{sku.Consumed} assigned against {sku.Enabled} enabled seats.",
                "Resolve the assignment inconsistency before the next true-up to avoid billing surprises.",
                Severity.High, 0));
        }

        var hasE3 = snapshot.HasLicenseContaining("E3") || snapshot.HasLicenseContaining("G3") || snapshot.HasLicenseContaining("A3");
        if (hasE3 && !snapshot.IsE5Class)
        {
            insights.Add(new LicenseInsight(
                "LIC-E5-GAP",
                "E3-class licensing limits Purview capability",
                "Insider Risk, advanced eDiscovery, Audit Premium, adaptive protection and advanced auto-labelling require E5/A5/G5 or add-ons.",
                "Either upgrade the in-scope population to E5, purchase the specific Purview add-ons, or record the affected controls as compensating manual controls.",
                Severity.High, 0));
        }

        if (snapshot.CopilotLicenses > 0 && !snapshot.IsE5Class)
        {
            insights.Add(new LicenseInsight(
                "LIC-COPILOT-PREREQ",
                "Copilot is assigned without E5-class data controls",
                $"{snapshot.CopilotLicenses} Copilot licence(s) exist but no E5/A5/G5 SKU was detected.",
                "Microsoft 365 Copilot requires a base licence plus sufficient data protection; confirm prerequisites and labels before rollout.",
                Severity.High, 0));
        }

        if (snapshot.CopilotLicenses > 0 && snapshot.SensitivityLabelCount < 4)
        {
            insights.Add(new LicenseInsight(
                "LIC-COPILOT-LABELS",
                "Copilot rollout outpaces labelling",
                "Copilot can surface any content the user can already access, including unlabelled sensitive files.",
                "Complete the label taxonomy and publish before expanding Copilot to more users.",
                Severity.High, 0));
        }

        var families = snapshot.Licenses
            .GroupBy(l => Family(l.PartNumber))
            .Where(g => g.Key != "Other" && g.Count() > 2)
            .ToList();

        foreach (var family in families)
        {
            insights.Add(new LicenseInsight(
                $"LIC-MIX-{family.Key}",
                $"Multiple {family.Key} SKUs in use",
                $"{family.Count()} {family.Key} SKUs: {string.Join(", ", family.Select(l => l.PartNumber))}.",
                "Consolidate overlapping SKUs to reduce cost and simplify capability reporting.",
                Severity.Low, 0));
        }

        return insights;
    }

    public static double UtilizationPercent(LicenseSku sku) =>
        sku.Enabled == 0 ? 0 : Math.Round(sku.Consumed * 100.0 / sku.Enabled, 1);

    private static string Family(string partNumber)
    {
        if (partNumber.Contains("E5", StringComparison.OrdinalIgnoreCase) || partNumber.Contains("A5", StringComparison.OrdinalIgnoreCase) || partNumber.Contains("G5", StringComparison.OrdinalIgnoreCase)) return "E5";
        if (partNumber.Contains("E3", StringComparison.OrdinalIgnoreCase) || partNumber.Contains("A3", StringComparison.OrdinalIgnoreCase) || partNumber.Contains("G3", StringComparison.OrdinalIgnoreCase)) return "E3";
        if (partNumber.Contains("PURVIEW", StringComparison.OrdinalIgnoreCase)) return "Purview";
        if (partNumber.Contains("COPILOT", StringComparison.OrdinalIgnoreCase)) return "Copilot";
        return "Other";
    }
}
