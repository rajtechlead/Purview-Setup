using System.IO;
using System.Net.Http;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Ai;
using PurviewCopilot.Application.Assessment;
using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Application.Modules;
using PurviewCopilot.Application.Reporting;
using PurviewCopilot.Application.Workspace;
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
        IClock clock)
    {
        Workspace = workspace;
        Store = store;
        Certificates = certificates;
        Copilot = copilot;
        Usage = usage;
        Verifier = verifier;
        Clock = clock;
    }

    public GovernanceWorkspace Workspace { get; }
    public SqliteStore Store { get; }
    public ICertificateService Certificates { get; }
    public CopilotService Copilot { get; }
    public IAiUsageStore Usage { get; }
    public ConnectionVerifier Verifier { get; }
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

        var collector = new CompositeTenantCollector(
            new GraphTenantCollector(tokens, http),
            new CompliancePowerShellCollector(Path.Combine(dataDirectory, "scripts")));

        var registry = new ModuleRegistry();
        var assessment = new AssessmentService(collector, registry, store, store, clock);
        var planBuilder = new ChangePlanBuilder(clock);
        var backups = new BackupService(store, clock);
        var executor = new ChangeExecutor(allowLiveExecution: false);

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
            collector, assessment, planBuilder, backups, store, store,
            exporters, settings, executor, store, clock);

        var verifier = new ConnectionVerifier(tokens, http);

        return new AppServices(workspace, store, certificates, copilot, store, verifier, clock);
    }
}
