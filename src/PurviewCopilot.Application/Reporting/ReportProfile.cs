using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Reporting;

/// <summary>Section selection for the audience-specific report types.</summary>
public static class ReportProfile
{
    public static bool IncludesFindings(ReportType type) => type is not ReportType.Executive;

    public static bool IncludesRecommendations(ReportType type) =>
        type is ReportType.Full or ReportType.Governance or ReportType.Remediation;

    public static bool IncludesFrameworkDetail(ReportType type) =>
        type is ReportType.Full or ReportType.Executive or ReportType.Governance or ReportType.Compliance;

    public static IEnumerable<Finding> Findings(AssessmentResult assessment, ReportType type) =>
        type switch
        {
            ReportType.Executive => Array.Empty<Finding>(),
            ReportType.Security => assessment.Findings
                .Where(f => f.Severity >= Severity.High)
                .OrderByDescending(f => f.Severity),
            ReportType.Compliance => assessment.Findings
                .Where(f => f.FrameworkRefs.Count > 0)
                .OrderByDescending(f => f.Severity),
            ReportType.Remediation => assessment.Findings
                .Where(f => f.Status is FindingStatus.Fail or FindingStatus.Warning)
                .OrderByDescending(f => f.Severity),
            _ => assessment.Findings.OrderByDescending(f => f.Severity).ThenBy(f => f.Id)
        };

    public static IEnumerable<Recommendation> Recommendations(AssessmentResult assessment, ReportType type) =>
        IncludesRecommendations(type)
            ? assessment.Recommendations.OrderByDescending(r => r.Priority)
            : Array.Empty<Recommendation>();
}
