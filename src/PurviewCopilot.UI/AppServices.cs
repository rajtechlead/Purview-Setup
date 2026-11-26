using System.IO;
using System.Net.Http;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Ai;
using PurviewCopilot.Application.Assessment;
using PurviewCopilot.Application.Auth;
using PurviewCopilot.Application.Monitoring;
using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Application.Modules;
using PurviewCopilot.Application.Reporting;
using PurviewCopilot.Application.Updates;
using PurviewCopilot.Application.Workspace;
using PurviewCopilot.Domain;
using PurviewCopilot.Infrastructure.Ai;
using PurviewCopilot.Infrastructure.Auth;
using PurviewCopilot.Infrastructure.Data;
using PurviewCopilot.Infrastructure.Deployment;
using PurviewCopilot.Infrastructure.Discovery;

namespace PurviewCopilot.UI;

/// <summary>
/// Explicit composition root. Manual wiring keeps startup obvious and avoids a container in a
/// desktop app where the graph of services is small and fixed.
/// </summary>
public sealed class AppServices
{
    private AppServices(
        GovernanceWorkspace workspace,
        SqliteStore store,
        ICertificateService certificates,
        CopilotService copilot,
        IAiUsageStore usage,
        ConnectionVerifier verifier,
        AuthenticationProvisioningService authProvisioning,
        PhasedDeploymentService phasedDeployment,
        ILiveDeploymentExecutor liveExecutor,
        ObjectBackupService objectBackups,
        UpdateChecker updates,
        Func<string, AuthConfiguration, IKeyVaultCertificateStore> keyVaultFactory,
        AlertNotifier alerts,
        IClock clock)
    {
        Workspace = workspace;
        Store = store;
        Certificates = certificates;
        Copilot = copilot;
        Usage = usage;
        Verifier = verifier;
        AuthProvisioning = authProvisioning;
        PhasedDeployment = phasedDeployment;
        LiveExecutor = liveExecutor;
        ObjectBackups = objectBackups;
        Updates = updates;
        KeyVaultFactory = keyVaultFactory;
        Alerts = alerts;
        Clock = clock;
    }

    public GovernanceWorkspace Workspace { get; }
    public SqliteStore Store { get; }
    public ICertificateService Certificates { get; }
    public CopilotService Copilot { get; }
    public IAiUsageStore Usage { get; }
    public ConnectionVerifier Verifier { get; }
    public AuthenticationProvisioningService AuthProvisioning { get; }
    public PhasedDeploymentService PhasedDeployment { get; }
    public ILiveDeploymentExecutor LiveExecutor { get; }
    public ObjectBackupService ObjectBackups { get; }
    public UpdateChecker Updates { get; }
    public Func<string, AuthConfiguration, IKeyVaultCertificateStore> KeyVaultFactory { get; }
    public AlertNotifier Alerts { get; }
    public IClock Clock { get; }

    public static AppServices Create()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PurviewGovernanceCopilot");

        Directory.CreateDirectory(dataDirectory);

        var clock = new SystemClock();
        var store = new SqliteStore(dataDirectory);
        var certificates = new WindowsCertificateService();
        var tokens = new EntraTokenProvider(certificates);
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        var updates = new UpdateChecker(http);
        var alerts = new AlertNotifier(http);
        Func<string, AuthConfiguration, IKeyVaultCertificateStore> keyVault = (uri, auth) =>
            new KeyVaultCertificateStore(uri, http, tokens.CreateCredential(auth));

        var collector = new CompositeTenantCollector(
            new GraphTenantCollector(tokens, http),
            new CompliancePowerShellCollector(Path.Combine(dataDirectory, "scripts")));

        var pluginDirectory = Path.Combine(dataDirectory, "plugins");
        Directory.CreateDirectory(pluginDirectory);
        var plugins = PluginModuleLoader.Load(pluginDirectory);
        var registry = new ModuleRegistry(plugins.Modules);
        var assessment = new AssessmentService(collector, registry, store, store, clock);
        var planBuilder = new ChangePlanBuilder(clock);
        var backups = new BackupService(store, clock);
        var executor = new ChangeExecutor(allowLiveExecution: false);
        var phasedDeployment = new PhasedDeploymentService(executor, backups, store, clock);
        var runner = new PowerShellRunner();
        var liveExecutor = new PowerShellDeploymentExecutor(runner, allowLiveExecution: false);
        var objectBackups = new ObjectBackupService(runner, backups, clock);

        var exporters = new IReportExporter[]
        {
            new HtmlReportExporter(),
            new JsonReportExporter(),
            new CsvReportExporter(),
            new MarkdownReportExporter(),
            new PdfReportExporter(),
            new DocxReportExporter(),
            new XlsxReportExporter()
        };

        var settings = new SqliteStore(dataDirectory);
        var orchestrator = new AiFailoverOrchestrator(
            new AiProviderFactory(http, new EnvironmentSecretResolver()),
            store,
            clock);

        var copilot = new CopilotService(orchestrator, settings, store, clock);

        var workspace = new GovernanceWorkspace(
            collector, assessment, planBuilder, backups, store, store, store, store,
            exporters, settings, executor, store, clock);

        var verifier = new ConnectionVerifier(tokens, http);
        var provisioner = new GraphAuthenticationProvisioner(tokens, verifier, http);
        var authProvisioning = new AuthenticationProvisioningService(provisioner, certificates, store, clock);

        return new AppServices(workspace, store, certificates, copilot, store, verifier, authProvisioning,
            phasedDeployment, liveExecutor, objectBackups, updates, keyVault, alerts, clock);
    }
}
