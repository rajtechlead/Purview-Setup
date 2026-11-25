using PurviewCopilot.Domain;

namespace PurviewCopilot.Domain.Scoring;

/// <summary>
/// Deterministic, testable scoring. No I/O, no randomness: the same findings always
/// produce the same scores, which is required for auditability of report output.
/// </summary>
public static class ScoringEngine
{
    public const double PassPoints = 100;
    public const double WarningPoints = 60;
    public const double FailPoints = 0;

    private static readonly ModuleKey[] SecurityModules =
    {
        ModuleKey.InformationProtection, ModuleKey.SensitivityLabels, ModuleKey.DataLossPrevention,
        ModuleKey.EndpointDlp, ModuleKey.Audit, ModuleKey.InsiderRisk
    };

    private static readonly ModuleKey[] GovernanceModules =
    {
        ModuleKey.DataLifecycle, ModuleKey.RecordsManagement, ModuleKey.Dspm,
        ModuleKey.ComplianceManager, ModuleKey.AiGovernance, ModuleKey.CopilotGovernance,
        ModuleKey.DataClassification
    };

    private static readonly ModuleKey[] ComplianceModules =
    {
        ModuleKey.DataLossPrevention, ModuleKey.CommunicationCompliance, ModuleKey.EDIscovery,
        ModuleKey.RecordsManagement, ModuleKey.Audit, ModuleKey.DataLifecycle
    };

    private static readonly ModuleKey[] AiModules =
    {
        ModuleKey.AiGovernance, ModuleKey.Dspm, ModuleKey.CopilotGovernance
    };

    private static readonly ModuleKey[] CopilotModules =
    {
        ModuleKey.SensitivityLabels, ModuleKey.DataLossPrevention, ModuleKey.Audit,
        ModuleKey.AiGovernance, ModuleKey.CopilotGovernance, ModuleKey.DataClassification
    };

    public static ScoreSet Compute(
        IEnumerable<Finding> findings,
        IReadOnlyDictionary<ModuleKey, double>? weights = null)
    {
        var list = findings as IReadOnlyCollection<Finding> ?? findings.ToList();
        var modules = new List<ModuleScore>();

        foreach (var module in Enum.GetValues<ModuleKey>())
        {
            var moduleFindings = list.Where(f => f.Module == module && f.Status != FindingStatus.NotAssessed).ToList();
            if (moduleFindings.Count == 0)
            {
                modules.Add(new ModuleScore(module, 0, 0, 0, 0, 0));
                continue;
            }

            var points = moduleFindings.Sum(f => f.Status switch
            {
                FindingStatus.Pass => PassPoints,
                FindingStatus.Warning => WarningPoints,
                _ => FailPoints
            });

            modules.Add(new ModuleScore(
                module,
                Math.Round(points / moduleFindings.Count, 2),
                moduleFindings.Count,
                moduleFindings.Count(f => f.Status == FindingStatus.Pass),
                moduleFindings.Count(f => f.Status == FindingStatus.Warning),
                moduleFindings.Count(f => f.Status == FindingStatus.Fail)));
        }

        double Weighted(IEnumerable<ModuleKey> keys)
        {
            var considered = keys
                .Select(k => modules.First(m => m.Module == k))
                .Where(m => m.Assessed > 0)
                .ToList();

            if (considered.Count == 0) return 0;

            var totalWeight = considered.Sum(m => weights?.GetValueOrDefault(m.Module, 1.0) ?? 1.0);
            if (totalWeight <= 0) return 0;

            return Math.Round(considered.Sum(m => m.Score * (weights?.GetValueOrDefault(m.Module, 1.0) ?? 1.0)) / totalWeight, 2);
        }

        var overall = Weighted(Enum.GetValues<ModuleKey>());
        var maturityScore = overall;
        var maturity = maturityScore switch
        {
            >= 85 => MaturityLevel.Optimized,
            >= 70 => MaturityLevel.Managed,
            >= 50 => MaturityLevel.Defined,
            >= 30 => MaturityLevel.Developing,
            _ => MaturityLevel.Initial
        };

        return new ScoreSet(
            Security: Weighted(SecurityModules),
            Governance: Weighted(GovernanceModules),
            Compliance: Weighted(ComplianceModules),
            Maturity: maturityScore,
            MaturityLevel: maturity,
            Risk: ComputeRisk(list),
            AiGovernance: Weighted(AiModules),
            CopilotReadiness: Weighted(CopilotModules),
            Overall: overall,
            Modules: modules);
    }

    /// <summary>
    /// Risk exposure 0–100 where higher is worse. Weighted per open finding by severity;
    /// warnings count at 40% of a failure. Capped so a single finding cannot saturate the score.
    /// </summary>
    public static double ComputeRisk(IEnumerable<Finding> findings)
    {
        double exposure = 0;

        foreach (var finding in findings)
        {
            if (finding.Status is FindingStatus.Pass or FindingStatus.NotAssessed) continue;

            var severityWeight = finding.Severity switch
            {
                Severity.Critical => 25,
                Severity.High => 15,
                Severity.Medium => 8,
                Severity.Low => 3,
                _ => 1
            };

            exposure += severityWeight * (finding.Status == FindingStatus.Warning ? 0.4 : 1.0);
        }

        return Math.Round(Math.Min(100, exposure), 2);
    }
}
