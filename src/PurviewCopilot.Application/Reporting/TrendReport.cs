using System.Globalization;
using System.Text;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Reporting;

public sealed record TrendPoint(
    DateTimeOffset RunUtc,
    double Overall,
    double Security,
    double Governance,
    double Compliance,
    double Risk);

/// <summary>Time-series report over stored assessments, oldest to newest.</summary>
public static class TrendReport
{
    public static IReadOnlyList<TrendPoint> Build(IEnumerable<AssessmentResult> history, int take = 12) =>
        history
            .OrderBy(r => r.RunUtc)
            .TakeLast(Math.Max(1, take))
            .Select(r => new TrendPoint(
                r.RunUtc, r.Scores.Overall, r.Scores.Security, r.Scores.Governance, r.Scores.Compliance, r.Scores.Risk))
            .ToList();

    public static string BuildMarkdown(
        string organization,
        string tenantId,
        IEnumerable<AssessmentResult> history,
        DateTimeOffset generatedUtc,
        int take = 12)
    {
        var points = Build(history, take);

        var builder = new StringBuilder();
        builder.AppendLine($"# Governance trend — {organization}");
        builder.AppendLine();
        builder.AppendLine($"Tenant: `{tenantId}`  ");
        builder.AppendLine($"Generated: {generatedUtc:u}  ");
        builder.AppendLine($"Assessments in series: {points.Count}");
        builder.AppendLine();

        if (points.Count == 0)
        {
            builder.AppendLine("No stored assessments are available for this tenant.");
            return builder.ToString();
        }

        var first = points[0];
        var last = points[^1];
        builder.AppendLine("## Direction");
        builder.AppendLine();
        builder.AppendLine($"- Overall: {first.Overall:0.#} → {last.Overall:0.#} ({Delta(first.Overall, last.Overall)})");
        builder.AppendLine($"- Security: {first.Security:0.#} → {last.Security:0.#} ({Delta(first.Security, last.Security)})");
        builder.AppendLine($"- Governance: {first.Governance:0.#} → {last.Governance:0.#} ({Delta(first.Governance, last.Governance)})");
        builder.AppendLine($"- Compliance: {first.Compliance:0.#} → {last.Compliance:0.#} ({Delta(first.Compliance, last.Compliance)})");
        builder.AppendLine($"- Risk: {first.Risk:0.#} → {last.Risk:0.#} ({Delta(first.Risk, last.Risk)}; lower is better)");
        builder.AppendLine();

        builder.AppendLine("## Series");
        builder.AppendLine();
        builder.AppendLine("| Assessment (UTC) | Overall | Security | Governance | Compliance | Risk |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");

        foreach (var point in points)
        {
            builder.AppendLine(
                $"| {point.RunUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} | " +
                $"{point.Overall:0.#} | {point.Security:0.#} | {point.Governance:0.#} | " +
                $"{point.Compliance:0.#} | {point.Risk:0.#} |");
        }

        return builder.ToString();
    }

    private static string Delta(double first, double last)
    {
        var delta = last - first;
        return $"{delta:+0.#;-0.#;0} points";
    }
}
