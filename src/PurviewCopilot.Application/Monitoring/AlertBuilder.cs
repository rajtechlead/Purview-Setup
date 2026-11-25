using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Monitoring;

/// <summary>An in-app monitoring alert derived from drift, score movement or critical failures.</summary>
public sealed record AlertItem(
    string Id,
    string Title,
    string Detail,
    Severity Severity,
    DateTimeOffset AtUtc);

public static class AlertBuilder
{
    /// <summary>
    /// Builds alerts for the current assessment. Alerts are deterministic: the same evidence
    /// produces the same list, so a refresh cannot invent history.
    /// </summary>
    public static IReadOnlyList<AlertItem> Build(
        AssessmentResult? current,
        AssessmentResult? previous,
        IReadOnlyList<DriftItem> drift,
        DateTimeOffset atUtc)
    {
        var alerts = new List<AlertItem>();

        foreach (var item in drift)
        {
            alerts.Add(new AlertItem(
                $"ALERT-{item.Id}",
                item.Title,
                $"{item.BaselineValue} → {item.CurrentValue}. {item.RecommendedAction}",
                item.Severity,
                atUtc));
        }

        if (current is not null && previous is not null)
        {
            var delta = current.Scores.Overall - previous.Scores.Overall;

            if (delta <= -5)
            {
                alerts.Add(new AlertItem(
                    "ALERT-SCORE-DROP",
                    "Overall score regressed",
                    $"{previous.Scores.Overall:0.#} → {current.Scores.Overall:0.#} ({delta:+0.#;-0.#}). Review the new failures before the next reporting cycle.",
                    delta <= -10 ? Severity.Critical : Severity.High,
                    atUtc));
            }
        }

        if (current is not null)
        {
            var critical = current.Findings
                .Where(f => f.Status == FindingStatus.Fail && f.Severity == Severity.Critical)
                .OrderBy(f => f.Id)
                .ToList();

            if (critical.Count > 0)
            {
                alerts.Add(new AlertItem(
                    "ALERT-CRITICAL-FAILURES",
                    $"{critical.Count} critical control(s) failing",
                    string.Join(", ", critical.Take(6).Select(f => f.Id)) +
                    (critical.Count > 6 ? " …" : string.Empty),
                    Severity.Critical,
                    atUtc));
            }
        }

        return alerts
            .OrderByDescending(a => a.Severity)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();
    }
}
