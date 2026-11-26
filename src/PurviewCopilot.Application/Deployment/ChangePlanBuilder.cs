using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

/// <summary>
/// Turns selected recommendations into a reviewable change plan with the full change-management
/// pack. Nothing is applied from here: plans are simulated, backed up and approved first.
/// </summary>
public sealed class ChangePlanBuilder
{
    private readonly IClock _clock;

    public ChangePlanBuilder(IClock clock) => _clock = clock;

    public ChangePlan Build(
        TenantSnapshot snapshot,
        DeploymentMode mode,
        IReadOnlyList<Recommendation> recommendations,
        string actor,
        bool simulationRequired = true)
    {
        if (recommendations.Count == 0)
        {
            throw new ArgumentException("A change plan requires at least one recommendation.", nameof(recommendations));
        }

        var operations = recommendations
            .SelectMany(r => r.Operations)
            .GroupBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var destructive = operations.Count(o => o.IsDestructive);
        var highPriority = recommendations.Count(r => r.Priority >= Severity.High);
        var modules = recommendations.Select(r => r.Module).Distinct().OrderBy(m => m.ToString()).ToList();

        var greenfield = mode == DeploymentMode.Greenfield;
        var impact = new ChangeImpact(
            ExecutiveSummary:
                greenfield
                    ? $"Baseline deployment for an empty tenant: this plan applies {operations.Count} foundational configuration change(s) across {modules.Count} Purview module(s) in a safe starting order. Changes are reversible and validated after application."
                    : $"This plan applies {operations.Count} configuration change(s) across {modules.Count} Purview module(s) to raise governance maturity and close {highPriority} high-or-critical gap(s). Changes are reversible and validated after application.",
            TechnicalSummary:
                $"Modules: {string.Join(", ", modules)}. Operations: {string.Join("; ", operations.Select(o => $"{o.Action} {o.Target}"))}.",
            BusinessImpact:
                "Improved protection of sensitive business data and a defensible compliance position. No user-visible outage is expected; enforcement steps may introduce prompts for users handling sensitive content.",
            UserImpact:
                highPriority > 0
                    ? "Users may see sensitivity label recommendations, policy tips and, after enforcement, blocked or warned actions on sensitive content. Communicate before enforcement."
                    : "Minimal user impact; most changes affect configuration and reporting only.",
            SecurityImpact:
                "Reduces egress paths for sensitive data and increases audit coverage. Enforcement is staged after simulation to avoid blocking legitimate work.",
            ComplianceImpact:
                "Closes mapped gaps against ISO 27001, NIST CSF, CIS Controls, SOC 2, HIPAA, GDPR and PCI-DSS as recorded in the assessment.",
            LicensingImpact:
                $"Requires: {string.Join(", ", recommendations.SelectMany(r => r.RequiredLicenses).Distinct(StringComparer.OrdinalIgnoreCase))}.",
            Risks: new[]
            {
                destructive > 0 ? $"{destructive} operation(s) are destructive and require CAB approval." : "No destructive operations in this plan.",
                "Over-blocking during enforcement may disrupt business workflows.",
                "Configuration drift if changes bypass change control after rollout."
            },
            ValidationSteps: new[]
            {
                "Re-run the assessment and confirm the targeted findings move to Pass or Warning.",
                "Verify policy state in the Purview portal and with Get-DlpCompliancePolicy / Get-LabelPolicy.",
                "Confirm audit events for the change are retrievable with Search-UnifiedAuditLog.",
                "Spot-check user experience with a pilot account for each affected workload."
            },
            RollbackSteps: new[]
            {
                "Restore the pre-change configuration backup captured automatically by the executor.",
                "Re-import label and policy definitions from the backup and republish.",
                "Verify each rolled-back object in the portal and re-run validation.",
                "Record the rollback in the approval history and risk register."
            });

        return new ChangePlan(
            Guid.NewGuid(),
            snapshot.TenantId,
            mode,
            $"Purview change plan — {snapshot.TenantName} — {_clock.UtcNow:yyyy-MM-dd HH:mm}",
            recommendations.Select(r => r.Id).ToList(),
            operations,
            impact,
            _clock.UtcNow,
            actor,
            simulationRequired || destructive > 0);
    }
}
