using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Modules;

public sealed record RuleOutcome(FindingStatus Status, string Evidence);

/// <summary>
/// A single declarative control. Rules are data: they can be licensed, mapped to frameworks,
/// unit-tested and re-used by assessment, recommendation and reporting without duplication.
/// </summary>
public sealed record ControlRule(
    string Id,
    ModuleKey Module,
    string Title,
    Severity Severity,
    Func<TenantSnapshot, RuleOutcome> Evaluate,
    string Remediation,
    IReadOnlyList<string> Frameworks,
    IReadOnlyList<string> RequiredLicenses)
{
    public static RuleOutcome Pass(string evidence) => new(FindingStatus.Pass, evidence);
    public static RuleOutcome Warn(string evidence) => new(FindingStatus.Warning, evidence);
    public static RuleOutcome Fail(string evidence) => new(FindingStatus.Fail, evidence);

    public Finding ToFinding(TenantSnapshot snapshot)
    {
        RuleOutcome outcome;
        try
        {
            outcome = Evaluate(snapshot);
        }
        catch (Exception ex)
        {
            outcome = new RuleOutcome(FindingStatus.NotAssessed, $"Evaluation failed: {ex.Message}");
        }

        return new Finding(Id, Module, Title, outcome.Status, Severity, outcome.Evidence, Remediation, Frameworks);
    }
}
