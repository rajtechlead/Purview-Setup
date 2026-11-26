using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

public sealed record ValidationCheck(string Name, bool Passed, string Detail, ModuleKey? Module = null);

public sealed record ValidationReport(bool Passed, IReadOnlyList<ValidationCheck> Checks)
{
    public string Summary => Passed
        ? $"Validation passed ({Checks.Count} checks)."
        : $"Validation failed: {string.Join("; ", Checks.Where(c => !c.Passed).Select(c => c.Name))}.";
}

/// <summary>Post-deployment validation against a fresh snapshot, scoped to the phase's controls.</summary>
public static class DeploymentValidation
{
    public static ValidationReport Validate(
        ChangePlan plan,
        TenantSnapshot before,
        TenantSnapshot after,
        IReadOnlyList<Modules.IModule>? modules = null)
    {
        var checks = new List<ValidationCheck>
        {
            Check("Tenant identity unchanged", before.TenantId == after.TenantId,
                $"{before.TenantId} → {after.TenantId}")
        };

        if (Touches(plan, ModuleKey.Audit))
        {
            checks.Add(Check("Unified audit enabled", after.UnifiedAuditEnabled, "Audit ingestion must stay enabled.",
                ModuleKey.Audit));
            checks.Add(Check("Audit retention not reduced", after.AuditRetentionDays >= before.AuditRetentionDays,
                $"{before.AuditRetentionDays} → {after.AuditRetentionDays} days", ModuleKey.Audit));
        }

        if (Touches(plan, ModuleKey.InformationProtection, ModuleKey.SensitivityLabels, ModuleKey.AutoLabeling))
        {
            checks.Add(Check("Sensitivity labels not reduced", after.SensitivityLabelCount >= before.SensitivityLabelCount,
                $"{before.SensitivityLabelCount} → {after.SensitivityLabelCount}", ModuleKey.SensitivityLabels));
            checks.Add(Check("Label policies not reduced", after.LabelPolicyCount >= before.LabelPolicyCount,
                $"{before.LabelPolicyCount} → {after.LabelPolicyCount}", ModuleKey.SensitivityLabels));
        }

        if (Touches(plan, ModuleKey.DataLossPrevention, ModuleKey.EndpointDlp))
        {
            checks.Add(Check("DLP policies not reduced", after.DlpPolicyCount >= before.DlpPolicyCount,
                $"{before.DlpPolicyCount} → {after.DlpPolicyCount}", ModuleKey.DataLossPrevention));
        }

        if (Touches(plan, ModuleKey.RecordsManagement, ModuleKey.DataLifecycle))
        {
            checks.Add(Check("Retention policies not reduced", after.RetentionPolicyCount >= before.RetentionPolicyCount,
                $"{before.RetentionPolicyCount} → {after.RetentionPolicyCount}", ModuleKey.DataLifecycle));
        }

        foreach (var module in modules ?? Array.Empty<Modules.IModule>())
        {
            if (!Touches(plan, module.Key)) continue;

            var beforeFailures = module.Rules.Count(rule => rule.ToFinding(before).Status != FindingStatus.Pass);
            var afterFailures = module.Rules.Count(rule => rule.ToFinding(after).Status != FindingStatus.Pass);
            checks.Add(Check(
                $"{module.Descriptor.DisplayName} control evidence did not regress",
                afterFailures <= beforeFailures,
                $"{beforeFailures} → {afterFailures} failing control(s)",
                module.Key));
        }

        checks.Add(Check("No new collection warnings", after.Warnings.Count <= before.Warnings.Count,
            $"{before.Warnings.Count} → {after.Warnings.Count}"));

        return new ValidationReport(checks.All(c => c.Passed), checks);
    }

    private static bool Touches(ChangePlan plan, params ModuleKey[] modules) =>
        plan.Operations.Any(o => modules.Contains(o.Module));

    private static ValidationCheck Check(string name, bool passed, string detail, ModuleKey? module = null) =>
        new(name, passed, detail, module);
}
