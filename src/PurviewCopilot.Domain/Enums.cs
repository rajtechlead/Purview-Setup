namespace PurviewCopilot.Domain;

/// <summary>Purview capability areas exposed as independent modules.</summary>
public enum ModuleKey
{
    InformationProtection = 1,
    SensitivityLabels,
    AutoLabeling,
    DataLossPrevention,
    EndpointDlp,
    DataClassification,
    RecordsManagement,
    DataLifecycle,
    Audit,
    InsiderRisk,
    CommunicationCompliance,
    EDIscovery,
    Dspm,
    ComplianceManager,
    AiGovernance,
    CopilotGovernance,

    /// <summary>External module loaded from the plugins folder; rules and descriptor come from the plugin.</summary>
    Custom = 99
}

public enum Severity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum FindingStatus
{
    NotAssessed = 0,
    Pass,
    Warning,
    Fail
}

public enum MaturityLevel
{
    Initial = 1,
    Developing = 2,
    Defined = 3,
    Managed = 4,
    Optimized = 5
}

public enum DeploymentMode
{
    AssessmentOnly = 0,
    GuidedDeployment,
    OneClickRecommended,
    Custom,
    Expert,
    Greenfield
}

/// <summary>
/// How far an operation can be undone. A backup restores updated configuration; a deletion cannot
/// be reconstructed from configuration alone and needs an explicit risk decision.
/// </summary>
public enum Reversibility
{
    Reversible = 0,
    Restorable,
    Irreversible
}

public enum OperationAction
{
    NoOp = 0,
    Create,
    Update,
    Delete,
    Validate
}

public enum ApprovalState
{
    Draft = 0,
    PendingSecurity,
    PendingCompliance,
    PendingCab,
    Approved,
    Rejected,
    Applied,
    RolledBack
}

public enum ApprovalStage
{
    Security = 0,
    Compliance,
    Cab
}

public enum ApprovalDecision
{
    Approve = 0,
    Reject,
    RequestChanges
}

public enum AiRedactionMode
{
    Redact = 0,
    Tokenize,
    Mask,
    Block
}

public enum SensitiveCategory
{
    Pii = 0,
    Secret,
    Credential,
    TenantIdentifier,
    CustomerData,
    Financial
}

public enum AiProviderKind
{
    OpenAi = 0,
    AzureOpenAi,
    Anthropic,
    Gemini,
    DeepSeek,
    Grok,
    OpenRouter,
    Ollama,
    LmStudio,
    OpenWebUi,
    Custom
}

public enum ReportType
{
    Full = 0,
    Executive,
    Governance,
    Security,
    Compliance,
    Remediation
}

public enum ReportFormat
{
    Html = 0,
    Json,
    Csv,
    Markdown,
    Pdf,
    Docx,
    Xlsx
}

public enum CloudEnvironment
{
    Commercial = 0,
    Gcc,
    GccHigh,
    Dod
}

public enum UserRole
{
    Viewer = 0,
    Operator,
    Approver,
    Administrator
}

public enum CertificateAlgorithm
{
    Rsa2048 = 0,
    Rsa3072,
    Rsa4096,
    EccP256,
    EccP384
}

public enum ProvisioningEnvironment
{
    Production = 0,
    Lab
}

/// <summary>Ordered delivery phases for a production change plan.</summary>
public enum DeploymentPhase
{
    Discovery = 1,
    MonitoringAndAudit,
    InformationProtection,
    DataLossPrevention,
    RecordsManagement,
    InsiderRiskAndAi
}

public enum PhaseStatus
{
    NotStarted = 0,
    Simulated,
    AwaitingApproval,
    Approved,
    BackedUp,
    Executing,
    Executed,
    Validated,
    Failed,
    Paused,
    RolledBack
}

public enum ActionStatus
{
    Open = 0,
    InProgress,
    Blocked,
    Done
}

public enum DriftKind
{
    Configuration = 0,
    Security,
    Compliance,
    Governance
}
