namespace PurviewCopilot.Domain;

/// <summary>A single assessed control outcome produced by a module.</summary>
public sealed record Finding(
    string Id,
    ModuleKey Module,
    string Title,
    FindingStatus Status,
    Severity Severity,
    string Evidence,
    string Remediation,
    IReadOnlyList<string> FrameworkRefs)
{
    public static Finding NotAssessed(string id, ModuleKey module, string title, Severity severity) =>
        new(id, module, title, FindingStatus.NotAssessed, severity, "No evidence collected.", string.Empty, Array.Empty<string>());
}

/// <summary>A proposed change derived from one or more findings.</summary>
public sealed record Recommendation(
    string Id,
    ModuleKey Module,
    string Title,
    string Rationale,
    Severity Priority,
    string Effort,
    string ChangeRisk,
    IReadOnlyList<string> RequiredLicenses,
    IReadOnlyList<ChangeOperation> Operations);

/// <summary>A reversible configuration operation. Payload is module-specific JSON.</summary>
public sealed record ChangeOperation(
    string Id,
    ModuleKey Module,
    OperationAction Action,
    string Target,
    string PayloadJson,
    bool IsDestructive,
    bool RequiresSimulation);

/// <summary>Impact assessment attached to a change plan (change-management pack).</summary>
public sealed record ChangeImpact(
    string ExecutiveSummary,
    string TechnicalSummary,
    string BusinessImpact,
    string UserImpact,
    string SecurityImpact,
    string ComplianceImpact,
    string LicensingImpact,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> ValidationSteps,
    IReadOnlyList<string> RollbackSteps);

public sealed record ChangePlan(
    Guid Id,
    string TenantId,
    DeploymentMode Mode,
    string Title,
    IReadOnlyList<string> RecommendationIds,
    IReadOnlyList<ChangeOperation> Operations,
    ChangeImpact Impact,
    DateTimeOffset CreatedUtc,
    string CreatedBy,
    bool SimulationRequired)
{
    public int DestructiveOperationCount => Operations.Count(o => o.IsDestructive);
}

/// <summary>Immutable backup captured before a plan is applied.</summary>
public sealed record ConfigurationBackup(
    Guid Id,
    string TenantId,
    string Label,
    string SchemaVersion,
    string PayloadJson,
    DateTimeOffset CreatedUtc,
    string CreatedBy);

public sealed record ApprovalEvent(
    Guid Id,
    Guid PlanId,
    ApprovalStage Stage,
    ApprovalDecision Decision,
    string Actor,
    string Comment,
    DateTimeOffset AtUtc);

public sealed record ApprovalRequest(
    Guid Id,
    Guid PlanId,
    ApprovalState State,
    int CurrentStageIndex,
    IReadOnlyList<ApprovalEvent> History,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>Scoring outcome for one module.</summary>
public sealed record ModuleScore(
    ModuleKey Module,
    double Score,
    int Assessed,
    int Passed,
    int Warnings,
    int Failed);

/// <summary>Aggregated scores shown on the executive dashboard.</summary>
public sealed record ScoreSet(
    double Security,
    double Governance,
    double Compliance,
    double Maturity,
    MaturityLevel MaturityLevel,
    double Risk,
    double AiGovernance,
    double CopilotReadiness,
    double Overall,
    IReadOnlyList<ModuleScore> Modules)
{
    public string Band => Overall switch
    {
        >= 85 => "Optimized",
        >= 70 => "Managed",
        >= 50 => "Defined",
        >= 30 => "Developing",
        _ => "Initial"
    };

    public static ScoreSet Empty { get; } = new(0, 0, 0, 0, MaturityLevel.Initial, 100, 0, 0, 0, Array.Empty<ModuleScore>());
}

public sealed record LicenseSku(string PartNumber, int Consumed, int Enabled);

/// <summary>Normalized tenant facts. Modules never call the network; they read this snapshot.</summary>
public sealed class TenantSnapshot
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string TenantDomain { get; set; } = string.Empty;
    public DateTimeOffset CollectedUtc { get; set; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<LicenseSku> Licenses { get; set; } = Array.Empty<LicenseSku>();

    public int UsersEnabled { get; set; }
    public int Guests { get; set; }
    public int Groups { get; set; }
    public int ServicePrincipals { get; set; }
    public int ServicePrincipalsWithSecrets { get; set; }
    public int AppRegistrationsWithSecrets { get; set; }
    public int DirectoryRoles { get; set; }
    public int DirectoryRoleAssignments { get; set; }
    public int GlobalAdministrators { get; set; }
    public int ConditionalAccessPolicies { get; set; }
    public int ConditionalAccessPoliciesEnabled { get; set; }
    public int ConditionalAccessMfaPolicies { get; set; }
    public double MfaRegistrationPercent { get; set; }
    public bool PimEnabled { get; set; }
    public double SecureScorePercent { get; set; }
    public double ComplianceScorePercent { get; set; }

    public int ExchangeMailboxes { get; set; }
    public int SharePointSites { get; set; }
    public int OneDriveAccounts { get; set; }
    public int TeamsCount { get; set; }

    public bool UnifiedAuditEnabled { get; set; }
    public int AuditRetentionDays { get; set; }
    public int AuditAlertPolicyCount { get; set; }

    public int SensitivityLabelCount { get; set; }
    public int LabelPolicyCount { get; set; }
    public int AutoLabelingRuleCount { get; set; }
    public bool LabelEncryptionInUse { get; set; }
    public int TrainableClassifierCount { get; set; }
    public int CustomSensitiveInfoTypeCount { get; set; }

    public int DlpPolicyCount { get; set; }
    public int DlpEnforcedCount { get; set; }
    public bool EndpointDlpEnabled { get; set; }
    public int DevicesOnboarded { get; set; }
    public bool BrowserDlpEnabled { get; set; }

    public int RetentionPolicyCount { get; set; }
    public int RetentionLabelCount { get; set; }
    public bool RetentionCoversAllLocations { get; set; }
    public bool DispositionReviewEnabled { get; set; }
    public bool RegulatoryRecordsEnabled { get; set; }

    public int InsiderRiskPolicyCount { get; set; }
    public bool InsiderRiskAnonymized { get; set; }
    public int CommunicationCompliancePolicyCount { get; set; }
    public int EdiscoveryCaseCount { get; set; }
    public bool EdiscoveryPremiumEnabled { get; set; }

    public bool DspmEnabled { get; set; }
    public bool DspmForAiEnabled { get; set; }
    public bool AiGovernancePolicyConfigured { get; set; }
    public int CopilotLicenses { get; set; }
    public int CopilotGovernanceControls { get; set; }

    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();

    public bool HasLicenseContaining(string token) =>
        Licenses.Any(l => l.PartNumber.Contains(token, StringComparison.OrdinalIgnoreCase));

    public bool IsE5Class => HasLicenseContaining("E5") || HasLicenseContaining("A5") || HasLicenseContaining("G5");
}

public sealed record AssessmentResult(
    Guid Id,
    string TenantId,
    DateTimeOffset RunUtc,
    string Mode,
    ScoreSet Scores,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<Recommendation> Recommendations,
    int RuleCount);

public sealed record AiProviderDescriptor(
    string Id,
    string DisplayName,
    AiProviderKind Kind,
    string Endpoint,
    string Model,
    string ApiKeyRef,
    bool IsLocal,
    bool Enabled,
    int Priority,
    double InputCostPer1KTokens,
    double OutputCostPer1KTokens,
    int MaxContextTokens);

public sealed record AiUsageRecord(
    Guid Id,
    string ProviderId,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    double EstimatedCostUsd,
    bool Succeeded,
    string? ErrorCode,
    DateTimeOffset AtUtc);

/// <summary>Operator-owned remediation record for one finding in one tenant.</summary>
public sealed record FindingAction(
    string FindingId,
    string TenantId,
    string Owner,
    DateTimeOffset? DueUtc,
    ActionStatus Status,
    string Notes,
    DateTimeOffset UpdatedUtc);

/// <summary>Whole-ledger aggregate, computed over every stored usage row.</summary>
public sealed record AiUsageTotals(
    int Requests,
    int FailedRequests,
    int PromptTokens,
    int CompletionTokens,
    double CostUsd,
    int Providers);

public sealed record AiRequest(
    string SystemPrompt,
    string UserPrompt,
    int MaxTokens = 1200,
    double Temperature = 0.2);

public sealed record AiResponse(
    string Content,
    string ProviderId,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    double EstimatedCostUsd);

public sealed record SensitiveMatch(
    string RuleId,
    SensitiveCategory Category,
    int Start,
    int Length,
    string Value);

public sealed record RedactionResult(
    string Text,
    IReadOnlyList<SensitiveMatch> Matches,
    IReadOnlyDictionary<string, string> TokenMap,
    bool Blocked)
{
    public bool ContainsSensitiveData => Matches.Count > 0;
}

public sealed record AuditEntry(
    Guid Id,
    DateTimeOffset AtUtc,
    string Actor,
    string Action,
    string Target,
    string Detail,
    string? CorrelationId);

/// <summary>Result of recomputing the local audit hash chain.</summary>
public sealed record AuditChainStatus(
    bool IsValid,
    int Checked,
    string? FirstInvalidId,
    string Message);

/// <summary>A customer tenant managed from this workstation (MSP mode).</summary>
public sealed record TenantAccess(string Identity, UserRole Role);

public sealed record TenantProfile(
    string TenantId,
    string DisplayName,
    AuthConfiguration Auth,
    DateTimeOffset UpdatedUtc,
    string? Owner = null,
    IReadOnlyList<TenantAccess>? Access = null);

public sealed record DriftItem(
    string Id,
    DriftKind Kind,
    string Title,
    string BaselineValue,
    string CurrentValue,
    Severity Severity,
    string RecommendedAction,
    ModuleKey Module);

public sealed record CertificateInfo(
    string Thumbprint,
    string Subject,
    string Issuer,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool HasPrivateKey,
    string StorageLocation)
{
    public int DaysUntilExpiry => (int)Math.Floor((NotAfter - DateTimeOffset.UtcNow).TotalDays);
    public bool IsExpiringSoon(int thresholdDays = 30) => DaysUntilExpiry <= thresholdDays;
}

/// <summary>
/// Request to create everything the app needs to authenticate: app registration, service
/// principal, credential, owners, Graph permissions and admin consent.
/// </summary>
public sealed record ProvisioningRequest(
    string TenantId,
    string DisplayName,
    AuthMode Mode,
    CertificateAlgorithm Algorithm,
    int CertificateMonths,
    int SecretMonths,
    ProvisioningEnvironment Environment,
    IReadOnlyList<string> Owners,
    bool DryRun,
    bool AcknowledgeLongLivedCertificate,
    AuthConfiguration BootstrapAuth,
    string Operator,
    string Approver,
    string ApproverRole);

public sealed record ProvisioningStep(string Name, string Status, string Detail);

public sealed record ProvisioningResult(
    bool Succeeded,
    string Summary,
    string? ApplicationId,
    string? ObjectId,
    string? ServicePrincipalId,
    string? Thumbprint,
    AuthConfiguration? Auth,
    string RollbackScript,
    IReadOnlyList<ProvisioningStep> Steps);

/// <summary>One controlled phase of a deployment; operations are partitioned by module.</summary>
public sealed record DeploymentPhaseDefinition(DeploymentPhase Phase, string Name, string Description, IReadOnlyList<ModuleKey> Modules);

public sealed record PhaseExecution(
    Guid Id,
    Guid PlanId,
    DeploymentPhase Phase,
    PhaseStatus Status,
    string ApprovedBy,
    string ApproverRole,
    DateTimeOffset UpdatedUtc,
    IReadOnlyList<string> Log);

public sealed record AuthConfiguration(
    AuthMode Mode,
    string TenantId,
    string ClientId,
    string? CertificateThumbprint,
    string? ClientSecret,
    string? ManagedIdentityClientId,
    CloudEnvironment Cloud = CloudEnvironment.Commercial)
{
    public static AuthConfiguration Empty { get; } =
        new(AuthMode.Certificate, string.Empty, string.Empty, null, null, null);
}

public enum AuthMode
{
    Certificate = 0,
    ManagedIdentity,
    Interactive,
    ClientSecret
}
