using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Reporting;

/// <summary>
/// Executive readiness mapping. Coverage is passing controls divided by assessed controls per
/// framework; gaps list failing or warning controls with their framework evidence reference.
/// </summary>
public static class FrameworkMapper
{
    public static readonly string[] Frameworks =
    {
        "ISO 27001", "NIST CSF", "CIS v8", "SOC 2", "HIPAA", "GDPR", "PCI-DSS", "ISO 42001", "NIST AI RMF"
    };

    public static IReadOnlyList<FrameworkCoverage> Map(IReadOnlyList<Finding> findings)
    {
        var coverage = new List<FrameworkCoverage>();

        foreach (var framework in Frameworks)
        {
            var relevant = findings
                .Where(f => f.FrameworkRefs.Any(r => r.StartsWith(framework, StringComparison.OrdinalIgnoreCase)))
                .Where(f => f.Status != FindingStatus.NotAssessed)
                .ToList();

            if (relevant.Count == 0) continue;

            var covered = relevant.Count(f => f.Status == FindingStatus.Pass);
            var gaps = relevant
                .Where(f => f.Status != FindingStatus.Pass)
                .Select(f => $"{f.Id} {f.Title}")
                .ToList();

            coverage.Add(new FrameworkCoverage(
                framework,
                Math.Round(covered * 100.0 / relevant.Count, 1),
                relevant.Count,
                covered,
                gaps));
        }

        return coverage;
    }
}
