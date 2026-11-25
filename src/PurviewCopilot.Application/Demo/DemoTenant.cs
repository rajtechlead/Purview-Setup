using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Demo;

/// <summary>
/// Clearly-labelled sample tenant used by demo mode and UI design-time. Never mixed with live
/// data: the collector result replaces it, and reports state when sample data is in use.
/// </summary>
public static class DemoTenant
{
    public static TenantSnapshot Create() => new()
    {
        TenantId = "00000000-0000-0000-0000-000000000000",
        TenantName = "contoso-sample (demo data)",
        CollectedUtc = DateTimeOffset.UtcNow,
        Licenses = new[]
        {
            new LicenseSku("SPE_E3", 820, 950),
            new LicenseSku("SPE_E5", 140, 150),
            new LicenseSku("COPILOT_M365", 60, 80),
            new LicenseSku("PURVIEW_DSPM", 0, 50)
        },
        UsersEnabled = 960,
        Guests = 210,
        Groups = 340,
        ServicePrincipals = 95,
        AppRegistrationsWithSecrets = 7,
        GlobalAdministrators = 6,
        ConditionalAccessPolicies = 9,
        MfaRegistrationPercent = 88.4,
        PimEnabled = false,
        SecureScorePercent = 64.2,
        ComplianceScorePercent = 58.0,
        ExchangeMailboxes = 940,
        SharePointSites = 412,
        OneDriveAccounts = 880,
        TeamsCount = 96,
        UnifiedAuditEnabled = true,
        AuditRetentionDays = 180,
        AuditAlertPolicyCount = 2,
        SensitivityLabelCount = 5,
        LabelPolicyCount = 1,
        AutoLabelingRuleCount = 2,
        LabelEncryptionInUse = true,
        TrainableClassifierCount = 3,
        CustomSensitiveInfoTypeCount = 1,
        DlpPolicyCount = 2,
        DlpEnforcedCount = 0,
        EndpointDlpEnabled = true,
        DevicesOnboarded = 310,
        BrowserDlpEnabled = false,
        RetentionPolicyCount = 1,
        RetentionLabelCount = 4,
        RetentionCoversAllLocations = false,
        DispositionReviewEnabled = false,
        RegulatoryRecordsEnabled = false,
        InsiderRiskPolicyCount = 1,
        InsiderRiskAnonymized = true,
        CommunicationCompliancePolicyCount = 0,
        EdiscoveryCaseCount = 1,
        EdiscoveryPremiumEnabled = false,
        DspmEnabled = true,
        DspmForAiEnabled = false,
        AiGovernancePolicyConfigured = false,
        CopilotLicenses = 60,
        CopilotGovernanceControls = 1,
        Warnings = new[] { "Demo data — not collected from a tenant." }
    };
}
