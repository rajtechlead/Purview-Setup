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
    CopilotGovernance
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
