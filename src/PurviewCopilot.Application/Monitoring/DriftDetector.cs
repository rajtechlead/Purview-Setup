using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Monitoring;

/// <summary>
/// Compares a stored baseline snapshot with a fresh collection. Deterministic and unit-tested;
/// drift on a control that protects data is escalated more than a cosmetic change.
/// </summary>
public static class DriftDetector
{
    public static IReadOnlyList<DriftItem> Compare(TenantSnapshot baseline, TenantSnapshot current)
    {
        var items = new List<DriftItem>();

        void Check(
            string id,
            ModuleKey module,
            DriftKind kind,
            string title,
            object baselineValue,
            object currentValue,
            bool regressed,
            Severity severity,
            string action)
        {
            if (!regressed) return;

            items.Add(new DriftItem(
                id, kind, title,
                Convert.ToString(baselineValue, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(currentValue, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                severity, action, module));
        }

        Check("DRF-001", ModuleKey.Audit, DriftKind.Security, "Unified audit log was disabled",
            baseline.UnifiedAuditEnabled, current.UnifiedAuditEnabled,
            baseline.UnifiedAuditEnabled && !current.UnifiedAuditEnabled,
            Severity.Critical,
            "Re-enable audit immediately and investigate who changed it.");

        Check("DRF-002", ModuleKey.Audit, DriftKind.Compliance, "Audit retention was shortened",
            $"{baseline.AuditRetentionDays}d", $"{current.AuditRetentionDays}d",
            current.AuditRetentionDays < baseline.AuditRetentionDays,
            Severity.High,
            "Restore the retention policy to the approved period.");

        Check("DRF-003", ModuleKey.SensitivityLabels, DriftKind.Governance, "Sensitivity labels were removed",
            baseline.SensitivityLabelCount, current.SensitivityLabelCount,
            current.SensitivityLabelCount < baseline.SensitivityLabelCount,
            Severity.High,
            "Restore removed labels from the configuration backup.");

        Check("DRF-004", ModuleKey.DataLossPrevention, DriftKind.Compliance, "DLP enforcement was reduced",
            baseline.DlpEnforcedCount, current.DlpEnforcedCount,
            current.DlpEnforcedCount < baseline.DlpEnforcedCount,
            Severity.Critical,
            "Return policies to enforce mode or record an approved exception.");

        Check("DRF-005", ModuleKey.EndpointDlp, DriftKind.Security, "Endpoint DLP was disabled",
            baseline.EndpointDlpEnabled, current.EndpointDlpEnabled,
            baseline.EndpointDlpEnabled && !current.EndpointDlpEnabled,
            Severity.Critical,
            "Re-enable endpoint DLP and check device onboarding policy.");

        Check("DRF-006", ModuleKey.EndpointDlp, DriftKind.Configuration, "Device onboarding coverage dropped",
            baseline.DevicesOnboarded, current.DevicesOnboarded,
            baseline.DevicesOnboarded > 0 && current.DevicesOnboarded < baseline.DevicesOnboarded * 0.9,
            Severity.Medium,
            "Investigate offboarded devices and re-onboard through Intune.");

        Check("DRF-007", ModuleKey.ComplianceManager, DriftKind.Security, "Global Administrator count increased",
            baseline.GlobalAdministrators, current.GlobalAdministrators,
            current.GlobalAdministrators > baseline.GlobalAdministrators,
            Severity.High,
            "Remove standing Global Administrator assignments; use role groups or PIM.");

        Check("DRF-008", ModuleKey.ComplianceManager, DriftKind.Security, "Conditional Access coverage decreased",
            baseline.ConditionalAccessPolicies, current.ConditionalAccessPolicies,
            current.ConditionalAccessPolicies < baseline.ConditionalAccessPolicies,
            Severity.High,
            "Restore the missing conditional access policy or record the approved change.");

        Check("DRF-009", ModuleKey.ComplianceManager, DriftKind.Security, "App registrations gained client secrets",
            baseline.AppRegistrationsWithSecrets, current.AppRegistrationsWithSecrets,
            current.AppRegistrationsWithSecrets > baseline.AppRegistrationsWithSecrets,
            Severity.Medium,
            "Migrate the new registrations to certificate credentials.");

        Check("DRF-010", ModuleKey.RecordsManagement, DriftKind.Governance, "Retention labels were removed",
            baseline.RetentionLabelCount, current.RetentionLabelCount,
            current.RetentionLabelCount < baseline.RetentionLabelCount,
            Severity.High,
            "Restore the file plan from backup and confirm disposition review.");

        if (current.SecureScorePercent + 5 < baseline.SecureScorePercent)
        {
            items.Add(new DriftItem(
                "DRF-011", DriftKind.Security, "Secure score dropped",
                $"{baseline.SecureScorePercent:0.#}%", $"{current.SecureScorePercent:0.#}%",
                Severity.Medium,
                "Review newly failing secure score actions and assign owners.",
                ModuleKey.ComplianceManager));
        }

        return items;
    }

    public static float DriftSeverityScore(IReadOnlyList<DriftItem> items) =>
        items.Count == 0 ? 0 : items.Sum(i => i.Severity switch
        {
            Severity.Critical => 25,
            Severity.High => 15,
            Severity.Medium => 8,
            Severity.Low => 3,
            _ => 1
        });
}
