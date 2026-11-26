using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Collects tenant facts. Implementations must be read-only.</summary>
public interface ITenantCollector
{
    Task<TenantSnapshot> CollectAsync(AuthConfiguration auth, CancellationToken cancellationToken = default);
}

public interface IAssessmentStore
{
    Task SaveAsync(AssessmentResult result, CancellationToken cancellationToken = default);
    Task<AssessmentResult?> GetLatestAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssessmentResult>> ListAsync(string tenantId, int take = 50, CancellationToken cancellationToken = default);
}

public interface IChangePlanStore
{
    Task SaveAsync(ChangePlan plan, CancellationToken cancellationToken = default);
    Task<ChangePlan?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChangePlan>> ListAsync(string tenantId, int take = 50, CancellationToken cancellationToken = default);
    Task UpdateStateAsync(Guid planId, string state, CancellationToken cancellationToken = default);
}

public interface IApprovalStore
{
    Task SaveAsync(ApprovalRequest request, CancellationToken cancellationToken = default);
    Task<ApprovalRequest?> GetByPlanAsync(Guid planId, CancellationToken cancellationToken = default);
}

public interface IBackupStore
{
    Task SaveAsync(ConfigurationBackup backup, CancellationToken cancellationToken = default);
    Task<ConfigurationBackup?> GetLatestAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConfigurationBackup>> ListAsync(string tenantId, int take = 20, CancellationToken cancellationToken = default);
}

public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEntry>> ReadAsync(int take = 200, CancellationToken cancellationToken = default);

    /// <summary>Recomputes the tamper-evident chain over every stored entry, oldest first.</summary>
    Task<AuditChainStatus> VerifyAsync(CancellationToken cancellationToken = default);
}

public interface IFindingActionStore
{
    Task SaveAsync(FindingAction action, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FindingAction>> ListAsync(string tenantId, CancellationToken cancellationToken = default);
}

public interface IAiUsageStore
{
    Task RecordAsync(AiUsageRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiUsageRecord>> ListAsync(string? providerId = null, int take = 500, CancellationToken cancellationToken = default);

    /// <summary>Aggregates every stored row, independent of any listing limit.</summary>
    Task<AiUsageTotals> GetTotalsAsync(CancellationToken cancellationToken = default);
}

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>Customer tenants managed by an MSP or consultant, isolated by tenant id.</summary>
public interface ITenantProfileStore
{
    Task SaveAsync(TenantProfile profile, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantProfile>> ListAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(string tenantId, CancellationToken cancellationToken = default);
}

public interface ICertificateService
{
    CertificateInfo CreateSelfSigned(string subject, int validMonths, bool installToStore);

    CertificateInfo CreateSelfSigned(
        string subject,
        int validMonths,
        bool installToStore,
        CertificateAlgorithm algorithm);

    /// <summary>Base64 of the DER public certificate, as Graph keyCredentials expects.</summary>
    string ExportPublicDerBase64(string thumbprint);
    CertificateInfo ImportFromPfx(string path, string? password, bool installToStore);
    void ExportToPfx(string thumbprint, string path, string? password);
    void Install(string thumbprint);
    void Remove(string thumbprint);
    CertificateInfo? FindByThumbprint(string thumbprint);
    IReadOnlyList<CertificateInfo> List();
    CertificateInfo Rotate(string subject, int validMonths, bool installToStore);
}

/// <summary>Creates and configures the Entra application used for app-only authentication.</summary>
public interface IAuthenticationProvisioningGateway
{
    Task<(string ApplicationId, string ObjectId)> CreateApplicationAsync(
        string displayName, AuthConfiguration bootstrap, CancellationToken cancellationToken = default);

    Task<string> CreateServicePrincipalAsync(
        string applicationId, AuthConfiguration bootstrap, CancellationToken cancellationToken = default);

    Task AddCertificateAsync(
        string objectId, string publicCertificateBase64, AuthConfiguration bootstrap, CancellationToken cancellationToken = default);

    Task<string> AddSecretAsync(
        string objectId, string displayName, int months, AuthConfiguration bootstrap, CancellationToken cancellationToken = default);

    Task AddOwnerAsync(
        string objectId, string owner, AuthConfiguration bootstrap, CancellationToken cancellationToken = default);

    Task GrantGraphAdminConsentAsync(
        string servicePrincipalId, AuthConfiguration bootstrap, CancellationToken cancellationToken = default);

    Task<(bool Ok, string Message)> ValidateAsync(
        AuthConfiguration auth, CancellationToken cancellationToken = default);

    Task DeleteApplicationAsync(
        string objectId, string servicePrincipalId, AuthConfiguration bootstrap, CancellationToken cancellationToken = default);
}

public interface IKeyVaultCertificateStore
{
    Task<string?> DownloadPfxBase64Async(string secretName, CancellationToken cancellationToken = default);
    Task UploadPfxBase64Async(string secretName, string pfxBase64, CancellationToken cancellationToken = default);
}

public interface IAiProvider
{
    string Id { get; }
    Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

public interface IAiProviderFactory
{
    IAiProvider Create(AiProviderDescriptor descriptor);
}

public interface IChangeExecutor
{
    Task<ExecutionResult> SimulateAsync(ChangePlan plan, CancellationToken cancellationToken = default);
    Task<ExecutionResult> ApplyAsync(ChangePlan plan, CancellationToken cancellationToken = default);

    /// <summary>Configuration-as-code: the executable PowerShell for an approved plan.</summary>
    string GenerateScript(ChangePlan plan);

    /// <summary>Configuration-as-code: the reversal script, with destructive steps commented out.</summary>
    string GenerateRollbackScript(ChangePlan plan);
}

public sealed record ExecutionResult(bool Succeeded, string Summary, IReadOnlyList<string> Log);

public sealed record PowerShellResult(int ExitCode, string Output, string Error);

/// <summary>Runs a reviewed PowerShell script with an explicit, minimal environment.</summary>
public interface IPowerShellRunner
{
    Task<PowerShellResult> RunAsync(
        string script,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken = default);
}

/// <summary>Executes an approved phase against a live tenant. Implementations must fail closed.</summary>
public interface ILiveDeploymentExecutor
{
    bool LiveExecutionEnabled { get; }

    /// <summary>Session-scoped opt-in. It never persists, so a restart returns to fail-closed.</summary>
    void SetLiveExecutionEnabled(bool enabled);

    Task<ExecutionResult> ExecuteAsync(
        ChangePlan plan,
        PhaseExecution execution,
        AuthConfiguration auth,
        TenantSnapshot tenant,
        DeploymentAuthorization authorization,
        CancellationToken cancellationToken = default);

    string BuildPreview(ChangePlan plan);
}

public interface IReportExporter
{
    ReportFormat Format { get; }
    string FileExtension { get; }
    string Render(ReportContext context);

    /// <summary>Binary formats override this; text formats default to UTF-8 of <see cref="Render"/>.</summary>
    byte[] RenderBytes(ReportContext context) => System.Text.Encoding.UTF8.GetBytes(Render(context));
}

public sealed record ReportContext(
    TenantSnapshot Tenant,
    AssessmentResult Assessment,
    string OrganizationName,
    string GeneratedBy,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<FrameworkCoverage> Frameworks,
    ReportType ReportType = ReportType.Full);

public sealed record FrameworkCoverage(string Framework, double Percent, int TotalControls, int CoveredControls, IReadOnlyList<string> Gaps);

/// <summary>Application settings persisted in SQLite as a single JSON document.</summary>
public sealed record AppSettings
{
    public AuthConfiguration Auth { get; set; } = AuthConfiguration.Empty;
    public AiRedactionMode RedactionMode { get; set; } = AiRedactionMode.Tokenize;
    public bool CloudAiEnabled { get; set; } = true;
    public bool OfflineMode { get; set; }
    public bool RequirePromptReview { get; set; } = true;
    public int CertificateExpiryWarningDays { get; set; } = 30;
    public string? KeyVaultUri { get; set; }
    public string? KeyVaultSecretName { get; set; }
    public string DataDirectory { get; set; } = "%LOCALAPPDATA%\\PurviewGovernanceCopilot";
    public string? ReportOutputDirectory { get; set; }

    /// <summary>HTTPS manifest used by the update check; empty disables it.</summary>
    public string? UpdateManifestUrl { get; set; }
    public DeploymentMode DefaultMode { get; set; } = DeploymentMode.AssessmentOnly;

    /// <summary>Minutes between automatic checks while the app is open; 0 disables them.</summary>
    public int MonitoringIntervalMinutes { get; set; }

    /// <summary>Optional HTTPS webhook that receives monitoring alerts.</summary>
    public string? AlertWebhookUrl { get; set; }

    public bool DemoDataEnabled { get; set; }

    /// <summary>Optional monthly price per seat, keyed by SKU part number, for licence opportunity values.</summary>
    public Dictionary<string, double> LicensePrices { get; set; } = new();

    public List<AiProviderDescriptor> Providers { get; set; } = new();

    public static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            Providers = new List<AiProviderDescriptor>
            {
                new("ollama-local", "Ollama (local)", AiProviderKind.Ollama, "http://localhost:11434", "llama3.1", string.Empty, true, true, 10, 0, 0, 8192),
                new("azure-openai", "Azure OpenAI", AiProviderKind.AzureOpenAi, string.Empty, "gpt-4o-mini", "AZURE_OPENAI_KEY", false, false, 20, 0.00015, 0.0006, 128000),
                new("openai", "OpenAI", AiProviderKind.OpenAi, "https://api.openai.com/v1", "gpt-4o-mini", "OPENAI_API_KEY", false, false, 30, 0.00015, 0.0006, 128000),
                new("anthropic", "Anthropic Claude", AiProviderKind.Anthropic, "https://api.anthropic.com/v1", "claude-3-5-sonnet-latest", "ANTHROPIC_API_KEY", false, false, 40, 0.003, 0.015, 200000),
                new("gemini", "Google Gemini", AiProviderKind.Gemini, "https://generativelanguage.googleapis.com/v1beta", "gemini-1.5-pro", "GEMINI_API_KEY", false, false, 50, 0.00125, 0.005, 1000000)
            }
        };
    }
}
