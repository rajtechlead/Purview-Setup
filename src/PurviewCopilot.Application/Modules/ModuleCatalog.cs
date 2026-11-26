using PurviewCopilot.Domain;
using static PurviewCopilot.Application.Modules.ControlRule;

namespace PurviewCopilot.Application.Modules;

/// <summary>
/// The control library. Every rule is evidence-based against a tenant snapshot and carries
/// remediation, framework mapping and licence requirements so reporting stays consistent.
/// </summary>
public static class ModuleCatalog
{
    private static readonly string[] Iso = { "ISO 27001 A.8.12", "ISO 27001 A.5.33" };
    private static readonly string[] Nist = { "NIST CSF PR.DS-1", "NIST 800-53 SC-28" };
    private static readonly string[] Cis = { "CIS v8 3.13" };
    private static readonly string[] Gdpr = { "GDPR Art. 32", "GDPR Art. 5(1)(f)" };
    private static readonly string[] Pci = { "PCI-DSS 3.4", "PCI-DSS 4.1" };
    private static readonly string[] Sox = { "SOC 2 CC6.1", "SOC 2 CC7.2" };

    public static IReadOnlyList<ControlRule> All { get; } = Build();

    private static List<ControlRule> Build()
    {
        var rules = new List<ControlRule>();

        void Add(
            string id,
            ModuleKey module,
            string title,
            Severity severity,
            Func<TenantSnapshot, RuleOutcome> evaluate,
            string remediation,
            string[] frameworks,
            string[] licenses)
        {
            rules.Add(new ControlRule(id, module, title, severity, evaluate, remediation, frameworks, licenses));
        }

        // ---------------- Audit ----------------
        Add("AUD-01", ModuleKey.Audit, "Unified audit log ingestion is enabled", Severity.Critical,
            s => s.UnifiedAuditEnabled ? Pass("Unified audit log is recording tenant activity.") : Fail("Unified audit log ingestion is disabled."),
            "Enable audit in the Purview portal: Audit > Start recording user and admin activity.",
            new[] { "ISO 27001 A.8.15", "NIST CSF DE.CM-1", "CIS v8 8.2" }, new[] { "E3" });

        Add("AUD-02", ModuleKey.Audit, "Audit retention meets a one-year baseline", Severity.High,
            s => s.AuditRetentionDays >= 365 ? Pass($"Longest audit retention policy is {s.AuditRetentionDays} days.")
                : Warn($"Longest audit retention policy is {s.AuditRetentionDays} days (baseline 365)."),
            "Create or extend an audit retention policy (Get-UnifiedAuditLogRetentionPolicy) to at least 365 days, longer where regulation requires.",
            new[] { "ISO 27001 A.8.15", "SOC 2 CC7.2", "PCI-DSS 10.7" }, new[] { "E3" });

        Add("AUD-03", ModuleKey.Audit, "Audit alert policies are configured", Severity.Medium,
            s => s.AuditAlertPolicyCount > 0 ? Pass($"{s.AuditAlertPolicyCount} audit alert policy(ies) present.") : Fail("No audit alert policies are configured."),
            "Create alert policies for privilege changes, mass download, DLP overrides and eDiscovery operations.",
            new[] { "NIST CSF DE.AE-2", "SOC 2 CC7.3" }, new[] { "E3" });

        // ---------------- Information protection ----------------
        Add("IPT-01", ModuleKey.InformationProtection, "Sensitivity labels exist", Severity.Critical,
            s => s.SensitivityLabelCount > 0 ? Pass($"{s.SensitivityLabelCount} sensitivity label(s) defined.") : Fail("No sensitivity labels are defined."),
            "Define a four-level taxonomy (Public, General, Confidential, Highly Confidential) and create the label set.",
            Iso.Concat(Gdpr).ToArray(), new[] { "E3", "Information Protection" });

        Add("IPT-02", ModuleKey.InformationProtection, "A label policy publishes labels to users", Severity.High,
            s => s.LabelPolicyCount > 0 ? Pass($"{s.LabelPolicyCount} label policy(ies) published.") : Fail("No label publishing policy exists."),
            "Publish the label set to a pilot group first, then to the organization.",
            new[] { "ISO 27001 A.5.13", "SOC 2 CC6.1" }, new[] { "E3" });

        Add("IPT-03", ModuleKey.InformationProtection, "Labels protect content with encryption", Severity.Medium,
            s => s.LabelEncryptionInUse ? Pass("At least one label applies encryption or content marking.") : Warn("Labels exist but none apply encryption or content marking."),
            "Add encryption and content marking to the Confidential and Highly Confidential labels.",
            Iso.Concat(Pci).ToArray(), new[] { "E3", "Azure Information Protection P2" });

        Add("IPT-04", ModuleKey.InformationProtection, "Sensitive content is prioritised for protection", Severity.High,
            s => s.CustomSensitiveInfoTypeCount > 0 || s.TrainableClassifierCount > 0
                ? Pass($"{s.TrainableClassifierCount} trainable classifier(s), {s.CustomSensitiveInfoTypeCount} custom SIT(s) available.")
                : Warn("Only built-in sensitive information types are available."),
            "Adopt the built-in SITs first, then add custom SITs or trainable classifiers where a documented gap exists.",
            new[] { "ISO 27001 A.8.12", "GDPR Art. 30" }, new[] { "E5" });

        // ---------------- Sensitivity labels ----------------
        Add("SEN-01", ModuleKey.SensitivityLabels, "Taxonomy has at least four levels", Severity.Medium,
            s => s.SensitivityLabelCount >= 4 ? Pass($"{s.SensitivityLabelCount} labels support a graduated taxonomy.") : Warn($"{s.SensitivityLabelCount} label(s) is too coarse for a graduated taxonomy."),
            "Use four top-level labels plus sub-labels for departmental or regulatory variants.",
            new[] { "ISO 27001 A.5.12", "NIST CSF ID.AM-5" }, new[] { "E3" });

        Add("SEN-02", ModuleKey.SensitivityLabels, "Labels enforce rather than advise", Severity.High,
            s => s.LabelPolicyCount > 0 && s.SensitivityLabelCount > 0 ? Pass("Label set is published with at least one policy.")
                : Fail("Label publishing is incomplete."),
            "Publish labels with mandatory justification and a default label for documents and email.",
            new[] { "ISO 27001 A.5.13", "SOC 2 CC6.1" }, new[] { "E3" });

        // ---------------- Auto-labelling ----------------
        Add("AUT-01", ModuleKey.AutoLabeling, "Auto-labelling is configured", Severity.High,
            s => s.AutoLabelingRuleCount > 0 ? Pass($"{s.AutoLabelingRuleCount} auto-labelling rule(s) configured.") : Fail("No auto-labelling rules are configured."),
            "Start with service-side auto-labelling in simulation for high-confidence SITs (credit card, national ID, health data).",
            new[] { "ISO 27001 A.8.12", "GDPR Art. 5(1)(f)" }, new[] { "E5" });

        Add("AUT-02", ModuleKey.AutoLabeling, "Auto-labelling covers multiple data types", Severity.Medium,
            s => s.AutoLabelingRuleCount >= 3 ? Pass($"{s.AutoLabelingRuleCount} rules cover distinct data types.") : Warn("Fewer than three auto-labelling rules limit coverage."),
            "Add rules for financial, identity and health data, each validated on a 200-item sample before enforcement.",
            Nist, new[] { "E5" });

        // ---------------- DLP ----------------
        Add("DLP-01", ModuleKey.DataLossPrevention, "DLP policies exist", Severity.Critical,
            s => s.DlpPolicyCount > 0 ? Pass($"{s.DlpPolicyCount} DLP policy(ies) configured.") : Fail("No DLP policies are configured."),
            "Create a baseline policy for PII, financial and credential data across Exchange, SharePoint, OneDrive and Teams.",
            Pci.Concat(Gdpr).ToArray(), new[] { "E3" });

        Add("DLP-02", ModuleKey.DataLossPrevention, "At least one DLP policy is enforced", Severity.Critical,
            s => s.DlpEnforcedCount > 0 ? Pass($"{s.DlpEnforcedCount} policy(ies) enforcing.") : Warn("All DLP policies are in simulation or disabled."),
            "Move from simulation to enforcement only after a 10-business-day clean simulation with under 5% false positives.",
            new[] { "PCI-DSS 3.4", "HIPAA 164.312(e)", "SOC 2 CC6.7" }, new[] { "E3" });

        Add("DLP-03", ModuleKey.DataLossPrevention, "DLP covers unmanaged browser upload", Severity.Medium,
            s => s.BrowserDlpEnabled ? Pass("Browser-based DLP integration is enabled.") : Warn("Browser uploads are not covered by DLP."),
            "Integrate Defender for Cloud Apps with Purview DLP for browser and generative AI app uploads.",
            new[] { "NIST CSF PR.DS-5", "CIS v8 3.13" }, new[] { "E5", "Defender for Cloud Apps" });

        Add("DLP-04", ModuleKey.DataLossPrevention, "DLP rule set is granular", Severity.Medium,
            s => s.DlpPolicyCount >= 3 ? Pass($"{s.DlpPolicyCount} policies separate data categories and audiences.")
                : Warn("A single policy for all data types reduces precision and blocks tuning."),
            "Separate policies by data category and audience so thresholds and actions can be tuned independently.",
            Nist, new[] { "E3" });

        // ---------------- Endpoint DLP ----------------
        Add("EDP-01", ModuleKey.EndpointDlp, "Endpoint DLP is enabled", Severity.High,
            s => s.EndpointDlpEnabled ? Pass("Endpoint DLP is enabled.") : Fail("Endpoint DLP is not enabled."),
            "Enable device onboarding in Purview and deploy the endpoint DLP configuration.",
            new[] { "ISO 27001 A.8.12", "CIS v8 3.13", "PCI-DSS 3.4" }, new[] { "E5" });

        Add("EDP-02", ModuleKey.EndpointDlp, "Devices are onboarded", Severity.High,
            s => s.DevicesOnboarded > 0 ? Pass($"{s.DevicesOnboarded} device(s) onboarded.") : Fail("No devices are onboarded for endpoint DLP."),
            "Onboard a pilot of 10 devices, verify audit-only behaviour, then expand through Intune.",
            new[] { "NIST CSF PR.AC-4" }, new[] { "E5" });

        Add("EDP-03", ModuleKey.EndpointDlp, "Device coverage is broad enough to matter", Severity.Medium,
            s => s.UsersEnabled == 0 ? Warn("User count unavailable; cannot compute device coverage.")
                : s.DevicesOnboarded >= s.UsersEnabled ? Pass("Onboarded devices cover the enabled user population.")
                : Warn($"Onboarded devices ({s.DevicesOnboarded}) are fewer than enabled users ({s.UsersEnabled})."),
            "Track onboarding coverage weekly and target 90% of in-scope managed devices.",
            new[] { "ISO 27001 A.8.1" }, new[] { "E5" });

        // ---------------- Data classification ----------------
        Add("CLS-01", ModuleKey.DataClassification, "Classification capability is active", Severity.High,
            s => s.SensitivityLabelCount > 0 && (s.AutoLabelingRuleCount > 0 || s.TrainableClassifierCount > 0)
                ? Pass("Labels and automated classification are both present.")
                : Warn("Classification relies on manual labelling only."),
            "Combine manual labels with SIT-based or classifier-based auto-labelling for coverage at scale.",
            new[] { "ISO 27001 A.8.12", "NIST CSF ID.AM-5" }, new[] { "E5" });

        Add("CLS-02", ModuleKey.DataClassification, "Classified data is governed downstream", Severity.Medium,
            s => s.SensitivityLabelCount > 0 && s.DlpPolicyCount > 0
                ? Pass("Labels are referenced by DLP controls.")
                : Warn("Classification is not yet connected to enforcement."),
            "Reference sensitivity labels as DLP conditions so classification drives protection.",
            Nist.Concat(Sox).ToArray(), new[] { "E3" });

        // ---------------- Records management ----------------
        Add("REC-01", ModuleKey.RecordsManagement, "Retention labels declare records", Severity.High,
            s => s.RetentionLabelCount > 0 ? Pass($"{s.RetentionLabelCount} retention label(s) defined.") : Fail("No retention labels are defined."),
            "Build a file plan with retention labels for each record class supplied by Legal.",
            new[] { "ISO 27001 A.5.33", "SOC 2 CC7.5", "GDPR Art. 5(1)(e)" }, new[] { "E5", "Records Management" });

        Add("REC-02", ModuleKey.RecordsManagement, "Disposition review is enabled", Severity.High,
            s => s.DispositionReviewEnabled ? Pass("Disposition review is configured.") : Fail("No disposition review stage is configured."),
            "Add a disposition review stage with named reviewers before any label deletes content permanently.",
            new[] { "ISO 27001 A.5.33", "SOC 2 CC7.5" }, new[] { "E5" });

        Add("REC-03", ModuleKey.RecordsManagement, "Regulatory records are locked", Severity.Medium,
            s => s.RegulatoryRecordsEnabled ? Pass("Regulatory record labels prevent editing and deletion.") : Warn("No regulatory record labels are configured."),
            "Apply regulatory record labels where regulation requires immutable content (financial, health, safety).",
            new[] { "PCI-DSS 3.4", "HIPAA 164.312(c)(1)" }, new[] { "E5" });

        // ---------------- Data lifecycle ----------------
        Add("DLM-01", ModuleKey.DataLifecycle, "A retention policy exists", Severity.High,
            s => s.RetentionPolicyCount > 0 ? Pass($"{s.RetentionPolicyCount} retention policy(ies) configured.") : Fail("No retention policy is configured."),
            "Create a baseline retention policy for Exchange, SharePoint, OneDrive and Teams.",
            new[] { "ISO 27001 A.5.33", "GDPR Art. 5(1)(e)" }, new[] { "E3" });

        Add("DLM-02", ModuleKey.DataLifecycle, "Retention covers all workloads", Severity.Medium,
            s => s.RetentionCoversAllLocations ? Pass("Retention covers Exchange, SharePoint, OneDrive and Teams.") : Warn("Retention does not cover every in-scope workload."),
            "Extend the baseline policy to every workload that holds in-scope content.",
            new[] { "SOC 2 CC7.5", "NIST CSF PR.IP-6" }, new[] { "E3" });

        // ---------------- Insider risk ----------------
        Add("IRM-01", ModuleKey.InsiderRisk, "Insider Risk policy is active", Severity.High,
            s => s.InsiderRiskPolicyCount > 0 ? Pass($"{s.InsiderRiskPolicyCount} insider risk policy(ies) configured.") : Fail("No insider risk policy is configured."),
            "Start with a departing-user data theft policy scoped to a pilot group, then expand.",
            new[] { "ISO 27001 A.7.2.2", "SOC 2 CC7.3", "NIST CSF DE.CM-3" }, new[] { "E5" });

        Add("IRM-02", ModuleKey.InsiderRisk, "Insider risk data is anonymized where required", Severity.Medium,
            s => s.InsiderRiskAnonymized ? Pass("Anonymization is enabled for insider risk alerts.") : Warn("Anonymization is disabled."),
            "Enable anonymization unless HR and Legal require named alerts, and record the decision.",
            new[] { "GDPR Art. 5(1)(c)", "ISO 27701 8.2" }, new[] { "E5" });

        Add("IRM-03", ModuleKey.InsiderRisk, "Insider Risk is licensed", Severity.Medium,
            s => s.IsE5Class ? Pass("Tenant has E5/A5/G5-class licensing.") : Warn("No E5/A5/G5 SKU detected; Insider Risk is likely unlicensed."),
            "Confirm entitlement with procurement; otherwise track insider risk as a compensating manual control.",
            new[] { "SOC 2 CC7.3" }, new[] { "E5" });

        // ---------------- Communication compliance ----------------
        Add("CCM-01", ModuleKey.CommunicationCompliance, "Communication Compliance is scoped", Severity.Medium,
            s => s.CommunicationCompliancePolicyCount > 0 ? Pass($"{s.CommunicationCompliancePolicyCount} policy(ies) configured.") : Warn("No communication compliance policy is configured."),
            "Scope a policy to regulated roles and reviewable content with named reviewers and retention.",
            new[] { "ISO 27001 A.8.15", "SOC 2 CC7.3", "HIPAA 164.312(b)" }, new[] { "E5" });

        Add("CCM-02", ModuleKey.CommunicationCompliance, "Review process is owned", Severity.Low,
            s => s.CommunicationCompliancePolicyCount > 0 ? Pass("Policies exist; confirm named reviewers in the RACI.")
                : Warn("No review process is evidenced."),
            "Document reviewers, escalation and retention for reviewed material.",
            new[] { "SOC 2 CC7.4" }, new[] { "E5" });

        // ---------------- eDiscovery ----------------
        Add("EDX-01", ModuleKey.EDIscovery, "eDiscovery process has been exercised", Severity.Low,
            s => s.EdiscoveryCaseCount > 0 ? Pass($"{s.EdiscoveryCaseCount} case(s) present.") : Warn("No eDiscovery case has been created; the legal process is untested."),
            "Run a tabletop or test case covering search, hold, review and export.",
            new[] { "ISO 27001 A.8.15", "SOC 2 CC7.4" }, new[] { "E3" });

        Add("EDX-02", ModuleKey.EDIscovery, "Advanced eDiscovery capacity is available", Severity.Medium,
            s => s.EdiscoveryPremiumEnabled ? Pass("Advanced eDiscovery is available.") : Warn("Advanced eDiscovery is not evidenced."),
            "Confirm premium entitlement for legal hold at scale and custodian review.",
            new[] { "SOC 2 CC7.4" }, new[] { "E5" });

        // ---------------- DSPM ----------------
        Add("DSP-01", ModuleKey.Dspm, "Data Security Posture Management is enabled", Severity.High,
            s => s.DspmEnabled ? Pass("DSPM is enabled.") : Fail("DSPM is not enabled."),
            "Enable DSPM to get a unified view of sensitive data, risk and remediation actions.",
            new[] { "ISO 27001 A.8.12", "NIST CSF ID.RA-5" }, new[] { "E5" });

        Add("DSP-02", ModuleKey.Dspm, "DSPM for AI monitors generative AI use", Severity.High,
            s => s.DspmForAiEnabled ? Pass("DSPM for AI is enabled.") : Fail("DSPM for AI is not enabled."),
            "Enable DSPM for AI to discover prompts and responses containing sensitive data.",
            new[] { "ISO 42001 A.8", "NIST AI RMF MP-4" }, new[] { "E5", "DSPM for AI" });

        Add("DSP-03", ModuleKey.Dspm, "Posture findings are connected to enforcement", Severity.Medium,
            s => s.DspmEnabled && s.DlpPolicyCount > 0 ? Pass("DSPM and DLP are both present.")
                : Warn("Posture insights are not yet connected to enforcement."),
            "Convert DSPM recommendations into DLP policies and label changes with owners and due dates.",
            new[] { "NIST CSF ID.RA-6" }, new[] { "E5" });

        // ---------------- Compliance Manager ----------------
        Add("CPM-01", ModuleKey.ComplianceManager, "Compliance score is managed", Severity.High,
            s => s.ComplianceScorePercent >= 70 ? Pass($"Compliance score is {s.ComplianceScorePercent:0.#}%.")
                : Warn($"Compliance score is {s.ComplianceScorePercent:0.#}% (target 70%)."),
            "Assign improvement actions to owners and review the score monthly against the target.",
            new[] { "ISO 27001 9.1", "SOC 2 CC4.1" }, new[] { "E3" });

        Add("CPM-02", ModuleKey.ComplianceManager, "Conditional Access protects admin and data access", Severity.High,
            s => s.ConditionalAccessPoliciesEnabled > 0
                ? Pass($"{s.ConditionalAccessPoliciesEnabled} of {s.ConditionalAccessPolicies} conditional access policy(ies) enabled; {s.ConditionalAccessMfaPolicies} enforce MFA.")
                : s.ConditionalAccessPolicies > 0
                    ? Warn($"{s.ConditionalAccessPolicies} conditional access policy(ies) exist but none is enabled.")
                    : Fail("No conditional access policies are configured."),
            "Require MFA and compliant devices for administrative and Purview roles.",
            new[] { "ISO 27001 A.8.5", "NIST CSF PR.AC-1", "CIS v8 6.3" }, new[] { "Entra ID P1" });

        Add("CPM-03", ModuleKey.ComplianceManager, "Privileged roles are limited", Severity.Medium,
            s => s.GlobalAdministrators is > 0 and <= 5 ? Pass($"{s.GlobalAdministrators} Global Administrator(s).")
                : s.GlobalAdministrators == 0 ? Warn("Global Administrator count unavailable.")
                : Fail($"{s.GlobalAdministrators} Global Administrators exceeds the limit of 5."),
            "Reduce standing Global Administrators and use Purview role groups for daily administration.",
            new[] { "ISO 27001 A.8.2", "CIS v8 5.4" }, new[] { "Entra ID P1" });

        Add("CPM-04", ModuleKey.ComplianceManager, "MFA registration is near complete", Severity.High,
            s => s.MfaRegistrationPercent >= 95 ? Pass($"MFA registered for {s.MfaRegistrationPercent:0.#}% of users.")
                : Warn($"MFA registered for {s.MfaRegistrationPercent:0.#}% of users (target 95%)."),
            "Run a registration campaign and enforce MFA through conditional access.",
            new[] { "CIS v8 6.5", "NIST CSF PR.AC-7", "PCI-DSS 8.4" }, new[] { "Entra ID P1" });

        Add("CPM-05", ModuleKey.ComplianceManager, "Privileged Identity Management is in use", Severity.Medium,
            s => s.PimEnabled
                ? Pass($"PIM eligibility schedules exist across {s.DirectoryRoles} directory role(s) and {s.DirectoryRoleAssignments} active assignment(s).")
                : Warn($"No PIM eligibility schedule was found ({s.DirectoryRoleAssignments} active role assignment(s))."),
            "Move Purview and Entra admin roles to eligible, time-bound assignments with approval.",
            new[] { "ISO 27001 A.8.2", "CIS v8 5.4" }, new[] { "Entra ID P2" });

        Add("CPM-06", ModuleKey.ComplianceManager, "Service principals avoid client secrets", Severity.High,
            s => s.ServicePrincipalsWithSecrets == 0 ? Pass("No service principal relies on a client secret.")
                : Warn($"{s.ServicePrincipalsWithSecrets} service principal(s) use client secrets."),
            "Replace service-principal secrets with certificates or federated credentials, starting with privileged apps.",
            new[] { "ISO 27001 A.8.24", "NIST CSF PR.AC-1" }, new[] { "Entra ID P1" });

        Add("CPM-07", ModuleKey.ComplianceManager, "App registrations avoid client secrets", Severity.High,
            s => s.AppRegistrationsWithSecrets == 0 ? Pass("No app registration relies on a client secret.")
                : Warn($"{s.AppRegistrationsWithSecrets} app registration(s) use client secrets."),
            "Migrate to certificate credentials and rotate certificates before expiry.",
            new[] { "ISO 27001 A.8.24", "OWASP ASVS 2.10.4" }, new[] { "Entra ID" });

        Add("CPM-08", ModuleKey.ComplianceManager, "Secure score is actively managed", Severity.Medium,
            s => s.SecureScorePercent >= 70 ? Pass($"Secure score is {s.SecureScorePercent:0.#}%.")
                : Warn($"Secure score is {s.SecureScorePercent:0.#}% (target 70%)."),
            "Prioritise secure score actions that protect identity and data, not points for their own sake.",
            new[] { "NIST CSF ID.RA-5", "CIS v8 4.1" }, new[] { "E3" });

        // ---------------- AI governance ----------------
        Add("AIG-01", ModuleKey.AiGovernance, "An AI usage policy is configured", Severity.High,
            s => s.AiGovernancePolicyConfigured ? Pass("An AI governance policy is configured.")
                : Fail("No AI usage policy is configured in Purview."),
            "Publish an acceptable-use policy for generative AI and enforce it with DLP and DSPM for AI.",
            new[] { "ISO 42001 A.5", "NIST AI RMF GV-1", "OWASP LLM Top 10 LLM06" }, new[] { "E5" });

        Add("AIG-02", ModuleKey.AiGovernance, "Sensitive prompts are blocked or audited", Severity.High,
            s => s.DspmForAiEnabled && s.DlpPolicyCount > 0 ? Pass("AI traffic is covered by DLP and DSPM.")
                : Warn("AI prompt/response data is not covered by DLP."),
            "Add DLP rules for generative AI apps and review DSPM for AI alerts weekly.",
            new[] { "NIST AI RMF MP-5", "OWASP LLM Top 10 LLM02" }, new[] { "E5" });

        Add("AIG-03", ModuleKey.AiGovernance, "AI inventory and ownership are documented", Severity.Medium,
            s => s.AiGovernancePolicyConfigured ? Pass("Governance policy provides an inventory baseline.")
                : Warn("No AI inventory or ownership record is evidenced."),
            "Record approved AI services, owners, data classes and review dates.",
            new[] { "ISO 42001 A.6", "NIST AI RMF GV-2" }, new[] { "E5" });

        // ---------------- Copilot governance ----------------
        Add("CPG-01", ModuleKey.CopilotGovernance, "Copilot licensing is known", Severity.Info,
            s => s.CopilotLicenses > 0 ? Pass($"{s.CopilotLicenses} Copilot licence(s) assigned.") : Warn("No Copilot licences assigned or not collected."),
            "Inventory Copilot licences and map them to data owners before enabling.",
            new[] { "NIST AI RMF GV-1" }, new[] { "Microsoft 365 Copilot" });

        Add("CPG-02", ModuleKey.CopilotGovernance, "Labels gate Copilot oversharing", Severity.High,
            s => s.SensitivityLabelCount >= 4 && s.LabelPolicyCount > 0 ? Pass("A graduated label set is published.")
                : Warn("Copilot readiness requires a published, graduated label set."),
            "Publish labels before Copilot rollout; Copilot honours label-based access restrictions.",
            new[] { "ISO 27001 A.5.13", "NIST AI RMF MP-3" }, new[] { "E5", "Microsoft 365 Copilot" });

        Add("CPG-03", ModuleKey.CopilotGovernance, "Audit captures Copilot interactions", Severity.Critical,
            s => s.UnifiedAuditEnabled ? Pass("Audit is enabled; Copilot interaction events are retained.")
                : Fail("Audit is disabled, so Copilot interactions cannot be investigated."),
            "Enable audit before Copilot pilot; retain Copilot events for the investigation window.",
            new[] { "ISO 27001 A.8.15", "SOC 2 CC7.2" }, new[] { "E3" });

        Add("CPG-04", ModuleKey.CopilotGovernance, "Copilot governance controls are applied", Severity.Medium,
            s => s.CopilotGovernanceControls >= 3 ? Pass($"{s.CopilotGovernanceControls} Copilot governance controls recorded.")
                : Warn($"{s.CopilotGovernanceControls} Copilot governance controls recorded (target 3+)."),
            "Restrict Copilot to approved data, block agent creation where unmanaged, and review weekly.",
            new[] { "ISO 42001 A.8", "NIST AI RMF MP-2" }, new[] { "Microsoft 365 Copilot" });

        return rules;
    }
}
