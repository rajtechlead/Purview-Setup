using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

public sealed record ConfigurationDebt(
    string Id,
    ModuleKey Module,
    Severity Severity,
    string Title,
    string Evidence,
    string Remediation);

public sealed record RemediationWave(string Name, string Rationale, IReadOnlyList<ConfigurationDebt> Items);

public sealed record BrownfieldAssessment(
    bool IsBrownfield,
    IReadOnlyList<string> ConfiguredAreas,
    IReadOnlyList<ConfigurationDebt> Debt,
    IReadOnlyList<RemediationWave> Waves);

/// <summary>
/// Existing tenants are not empty: some controls are configured, some are half-done and some
/// contradict each other. This reads the collected counts, names the debt with the evidence that
/// produced it, and groups the fixes into three increasing waves so remediation stays incremental.
/// </summary>
public static class BrownfieldAssessmentBuilder
{
    public static BrownfieldAssessment Build(TenantSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var areas = ConfiguredAreas(snapshot);
        var debt = FindDebt(snapshot);

        var waves = new[]
        {
            new RemediationWave(
                "Wave 1 — stop exposure",
                "Controls that are missing while data is already managed are fixed first.",
                debt.Where(d => d.Severity == Severity.Critical).ToArray()),
            new RemediationWave(
                "Wave 2 — finish what exists",
                "Half-configured controls are completed before anything new is introduced.",
                debt.Where(d => d.Severity == Severity.High).ToArray()),
            new RemediationWave(
                "Wave 3 — close the gaps",
                "Improvements that do not leave data exposed are scheduled last.",
                debt.Where(d => d.Severity < Severity.High).ToArray())
        }.Where(w => w.Items.Count > 0).ToArray();

        return new BrownfieldAssessment(areas.Count > 0, areas, debt, waves);
    }

    private static IReadOnlyList<string> ConfiguredAreas(TenantSnapshot s)
    {
        var areas = new List<string>();
        if (s.UnifiedAuditEnabled) areas.Add("Audit");
        if (s.SensitivityLabelCount > 0) areas.Add("Sensitivity labels");
        if (s.LabelPolicyCount > 0) areas.Add("Label policies");
        if (s.AutoLabelingRuleCount > 0) areas.Add("Auto-labelling");
        if (s.DlpPolicyCount > 0) areas.Add("DLP");
        if (s.EndpointDlpEnabled) areas.Add("Endpoint DLP");
        if (s.RetentionPolicyCount > 0) areas.Add("Retention policies");
        if (s.RetentionLabelCount > 0) areas.Add("Retention labels");
        if (s.InsiderRiskPolicyCount > 0) areas.Add("Insider risk");
        if (s.CommunicationCompliancePolicyCount > 0) areas.Add("Communication compliance");
        if (s.EdiscoveryCaseCount > 0) areas.Add("eDiscovery");
        if (s.CopilotGovernanceControls > 0) areas.Add("Copilot governance");
        return areas;
    }

    private static IReadOnlyList<ConfigurationDebt> FindDebt(TenantSnapshot s)
    {
        var debt = new List<ConfigurationDebt>();

        void Add(string id, ModuleKey module, Severity severity, string title, string evidence, string remediation) =>
            debt.Add(new ConfigurationDebt(id, module, severity, title, evidence, remediation));

        if (!s.UnifiedAuditEnabled && (s.SensitivityLabelCount > 0 || s.DlpPolicyCount > 0 || s.RetentionPolicyCount > 0))
        {
            Add("BRD-01", ModuleKey.Audit, Severity.Critical,
                "Purview is configured while the unified audit log is off",
                $"{s.SensitivityLabelCount} label(s), {s.DlpPolicyCount} DLP policy/policies, {s.RetentionPolicyCount} retention policy/policies",
                "Enable the unified audit log before changing anything else; there is no evidence trail today.");
        }

        if (s.DlpPolicyCount > 0 && s.DlpEnforcedCount == 0)
        {
            Add("BRD-02", ModuleKey.DataLossPrevention, Severity.Critical,
                "Every DLP policy is still in test mode",
                $"{s.DlpPolicyCount} policy/policies, 0 enforcing",
                "Move the highest-volume policy to enforce with a documented exception process.");
        }
        else if (s.DlpEnforcedCount > 0 && s.DlpEnforcedCount < s.DlpPolicyCount)
        {
            Add("BRD-03", ModuleKey.DataLossPrevention, Severity.High,
                "Only some DLP policies enforce",
                $"{s.DlpEnforcedCount} of {s.DlpPolicyCount} enforce",
                "Review the remaining policies for blockers, then enforce or retire them.");
        }

        if (s.SensitivityLabelCount > 0 && s.LabelPolicyCount == 0)
        {
            Add("BRD-04", ModuleKey.SensitivityLabels, Severity.High,
                "Labels exist but no label policy publishes them",
                $"{s.SensitivityLabelCount} label(s), 0 policies",
                "Publish the taxonomy to the intended workloads before asking users to label.");
        }

        if (s.SensitivityLabelCount > 0 && s.AutoLabelingRuleCount == 0 && s.TrainableClassifierCount > 0)
        {
            Add("BRD-05", ModuleKey.AutoLabeling, Severity.Medium,
                "Classifiers are trained but nothing auto-labels",
                $"{s.TrainableClassifierCount} classifier(s), 0 rules",
                "Add auto-labelling for at least the highest-risk classifier.");
        }

        if (s.RetentionPolicyCount > 0 && !s.RetentionCoversAllLocations)
        {
            Add("BRD-06", ModuleKey.RecordsManagement, Severity.High,
                "Retention does not cover every workload",
                $"{s.RetentionPolicyCount} policy/policies, coverage is partial",
                "Extend the retention policy to the remaining locations or record why they are excluded.");
        }

        if (s.RetentionLabelCount > 0 && !s.DispositionReviewEnabled)
        {
            Add("BRD-07", ModuleKey.RecordsManagement, Severity.Medium,
                "Retention labels dispose without review",
                $"{s.RetentionLabelCount} label(s), disposition review off",
                "Enable disposition review where regulatory obligations require a human decision.");
        }

        if (s.DlpPolicyCount > 0 && !s.EndpointDlpEnabled)
        {
            Add("BRD-08", ModuleKey.EndpointDlp, Severity.High,
                "DLP stops at the cloud boundary",
                $"{s.DlpPolicyCount} DLP policy/policies, endpoint DLP off",
                "Onboard devices and enable endpoint DLP, or document the accepted gap.");
        }

        if (s.EdiscoveryCaseCount > 0 && !s.EdiscoveryPremiumEnabled)
        {
            Add("BRD-09", ModuleKey.EDIscovery, Severity.Low,
                "Cases exist without premium eDiscovery",
                $"{s.EdiscoveryCaseCount} case(s)",
                "Confirm standard eDiscovery meets the legal team's hold and review needs.");
        }

        return debt;
    }
}
