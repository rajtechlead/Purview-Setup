using System.Net.Http;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Assessment;
using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Application.Modules;
using PurviewCopilot.Application.Release;
using PurviewCopilot.Application.Reporting;
using PurviewCopilot.Application.Workspace;
using PurviewCopilot.Collaboration;
using PurviewCopilot.Domain;
using PurviewCopilot.Infrastructure.Auth;
using PurviewCopilot.Infrastructure.Data;
using PurviewCopilot.Infrastructure.Deployment;
using PurviewCopilot.Infrastructure.Discovery;
using Microsoft.EntityFrameworkCore;

namespace PurviewCopilot.Cli;

/// <summary>
/// Headless pipeline entry point. Reuses the same workspace and ports as the desktop shell, so
/// CLI assessments are stored, scored and reported identically.
/// </summary>
internal static class Program
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();

        try
        {
            var options = ParseOptions(args.Skip(1).ToArray());

            return command switch
            {
                "assess" => await AssessAsync(options),
                "decision-pack" => await DecisionPackAsync(options),
                "store-copy" => await StoreCopyAsync(options),
                "provenance" => Provenance(options),
                "verify-audit" => await VerifyAuditAsync(options),
                _ => Unknown(command)
            };
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Provenance(Dictionary<string, string?> options)
    {
        var output = options.GetValueOrDefault("output") ?? "artifacts/release-manifest.json";
        var artefacts = (options.GetValueOrDefault("files") ?? string.Empty)
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var commit = options.GetValueOrDefault("commit")
            ?? Environment.GetEnvironmentVariable("GITHUB_SHA")
            ?? string.Empty;

        var manifest = ReleaseManifest.Create(
            options.GetValueOrDefault("version") ?? "1.0.0",
            commit,
            DateTimeOffset.UtcNow,
            artefacts,
            options.GetValueOrDefault("sbom"));

        var full = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, manifest.ToJson());

        Console.WriteLine($"Wrote {full}: {manifest.Files.Count} artefact(s), SBOM {(manifest.Sbom is null ? "absent" : manifest.Sbom)}.");
        return 0;
    }

    private static async Task<int> AssessAsync(Dictionary<string, string?> options)
    {
        var (workspace, _) = CreateWorkspace(options, live: !options.ContainsKey("demo"));

        AssessmentResult assessment;

        if (options.ContainsKey("demo"))
        {
            assessment = await workspace.AssessSampleAsync();
        }
        else
        {
            var tenant = Require(options, "tenant");
            var client = Require(options, "client");
            var thumbprint = options.GetValueOrDefault("thumbprint");
            var secret = options.GetValueOrDefault("client-secret");

            if (string.IsNullOrWhiteSpace(thumbprint) && string.IsNullOrWhiteSpace(secret))
            {
                throw new ArgumentException("--thumbprint or --client-secret is required unless --demo is used.");
            }

            var mode = ParseMode(options.GetValueOrDefault("mode"));
            var auth = new AuthConfiguration(
                string.IsNullOrWhiteSpace(thumbprint) ? AuthMode.ClientSecret : AuthMode.Certificate,
                tenant,
                client,
                thumbprint,
                secret,
                null);

            assessment = await workspace.AssessAsync(auth, mode);
        }

        var snapshot = workspace.Tenant ?? throw new InvalidOperationException("No tenant snapshot was collected.");

        var format = ParseFormat(options.GetValueOrDefault("format") ?? "json");
        var exporter = Exporters().FirstOrDefault(e => e.Format == format)
            ?? throw new ArgumentException($"Unsupported report format '{format}'.");

        var context = new ReportContext(
            snapshot,
            assessment,
            options.GetValueOrDefault("org") ?? snapshot.TenantName,
            options.GetValueOrDefault("by") ?? Environment.UserName,
            DateTimeOffset.UtcNow,
            workspace.Frameworks);

        var output = options.GetValueOrDefault("out")
            ?? Path.Combine(
                Directory.GetCurrentDirectory(),
                $"purview-{SafeFilePart(snapshot.TenantId)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}{exporter.FileExtension}");

        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllBytesAsync(output, exporter.RenderBytes(context));

        var critical = assessment.Findings.Count(f => f.Status == FindingStatus.Fail && f.Severity == Severity.Critical);

        Console.WriteLine($"Tenant      : {snapshot.TenantName} ({snapshot.TenantId})");
        Console.WriteLine($"Overall     : {assessment.Scores.Overall:0.#} · maturity {assessment.Scores.MaturityLevel}");
        Console.WriteLine($"Findings    : {assessment.Findings.Count} · critical failures {critical}");
        Console.WriteLine($"Stored      : assessment {assessment.Id} ({assessment.RuleCount} rules)");
        Console.WriteLine($"Report      : {output}");

        return 0;
    }

    private static async Task<int> DecisionPackAsync(Dictionary<string, string?> options)
    {
        if (!options.ContainsKey("demo"))
        {
            throw new ArgumentException("decision-pack only supports --demo until a plan export carries its assessment.");
        }

        var (workspace, _) = CreateWorkspace(options, live: false);
        var by = options.GetValueOrDefault("by") ?? Environment.UserName;

        var assessment = await workspace.AssessSampleAsync();
        var plan = await workspace.CreatePlanAsync(
            assessment.Recommendations, DeploymentMode.GuidedDeployment, by);

        var prices = ParsePrices(options.GetValueOrDefault("prices"));
        var pack = workspace.BuildDecisionPack(plan, prices);

        var output = Path.GetFullPath(options.GetValueOrDefault("out") ?? "decision-pack.md");
        await workspace.ExportDecisionPackAsync(plan, output, by, prices);

        Console.WriteLine($"Plan        : {plan.Id} · {plan.Operations.Count} operation(s)");
        Console.WriteLine($"Options     : {string.Join(", ", pack.Options.Select(o => $"{o.Key} ({o.Items.Count})"))}");
        Console.WriteLine($"Phases      : {pack.Phases.Count} · owners {pack.Owners.Count}");
        Console.WriteLine($"Costs       : {(pack.Costs.Count == 0 ? "no licences" : string.Join(", ", pack.Costs.Select(c => $"{c.Licence}={c.Total?.ToString() ?? "?"}")))}");
        Console.WriteLine($"Decision pack: {output}");

        return 0;
    }

    private static IReadOnlyDictionary<string, decimal>? ParsePrices(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('=', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !decimal.TryParse(parts[1], out var price))
            {
                throw new ArgumentException($"Could not read price '{entry}'. Use Licence=Price pairs separated by ';'.");
            }

            prices[parts[0]] = price;
        }

        return prices;
    }

    private static async Task<int> StoreCopyAsync(Dictionary<string, string?> options)
    {
        var sourcePath = Path.GetFullPath(Require(options, "source"));
        if (!File.Exists(sourcePath))
        {
            throw new ArgumentException($"Source database '{sourcePath}' does not exist.");
        }

        var targetConnection = Require(options, "target");
        var provider = options.GetValueOrDefault("target-provider") ?? "SqlServer";

        if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFullPath(targetConnection.Replace("Data Source=", string.Empty).Trim()) == sourcePath)
        {
            throw new ArgumentException("Source and target must be different databases.");
        }

        await using var source = new CollaborationDbContext(
            CollaborationStoreFactory.FromConfiguration("Sqlite", $"Data Source={sourcePath}"));
        await using var target = new CollaborationDbContext(
            CollaborationStoreFactory.FromConfiguration(provider, targetConnection));

        await source.Database.MigrateAsync();
        await target.Database.MigrateAsync();

        var report = await StoreCopier.CopyAsync(source, target);

        Console.WriteLine($"Source      : {sourcePath}");
        Console.WriteLine($"Target      : {provider}");
        Console.WriteLine($"Copied      : {report.Total} row(s) — memberships {report.Memberships}, assessments {report.Assessments}, plans {report.Plans}, approvals {report.Approvals}, audit {report.AuditEntries}, schema {report.SchemaMarkers}");
        Console.WriteLine("Point the service at the target with Collaboration:Provider and Collaboration:ConnectionString, then stop using the single-node database.");
        return 0;
    }

    private static async Task<int> VerifyAuditAsync(Dictionary<string, string?> options)
    {
        var (_, store) = CreateWorkspace(options);
        var status = await ((IAuditLog)store).VerifyAsync();

        Console.WriteLine(status.Message);
        return status.IsValid ? 0 : 2;
    }

    private static (GovernanceWorkspace Workspace, SqliteStore Store) CreateWorkspace(
        Dictionary<string, string?> options, bool live = true)
    {
        var dataDirectory = options.GetValueOrDefault("data-dir")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PurviewGovernanceCopilot");

        Directory.CreateDirectory(dataDirectory);

        var clock = new SystemClock();
        var store = new SqliteStore(dataDirectory);

        ITenantCollector collector;
        if (live)
        {
            var tokens = new EntraTokenProvider(new WindowsCertificateService());
            collector = new CompositeTenantCollector(
                new GraphTenantCollector(tokens, Http),
                new CompliancePowerShellCollector(Path.Combine(dataDirectory, "scripts")));
        }
        else
        {
            collector = new OfflineTenantCollector();
        }

        var pluginDirectory = Path.Combine(dataDirectory, "plugins");
        Directory.CreateDirectory(pluginDirectory);
        var registry = new ModuleRegistry(PluginModuleLoader.Load(pluginDirectory).Modules);
        var assessment = new AssessmentService(collector, registry, store, store, clock);

        var workspace = new GovernanceWorkspace(
            collector,
            assessment,
            new ChangePlanBuilder(clock),
            new BackupService(store, clock),
            store,
            store,
            store,
            store,
            Exporters(),
            store,
            new ChangeExecutor(allowLiveExecution: false),
            store,
            clock);

        return (workspace, store);
    }

    /// <summary>Keeps offline commands (demo assessment, decision pack) runnable off Windows.</summary>
    private sealed class OfflineTenantCollector : ITenantCollector
    {
        public Task<TenantSnapshot> CollectAsync(AuthConfiguration auth, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Live tenant collection requires a Windows certificate store.");
    }

    private static IReportExporter[] Exporters() =>
    [
        new HtmlReportExporter(),
        new JsonReportExporter(),
        new CsvReportExporter(),
        new MarkdownReportExporter(),
        new PdfReportExporter(),
        new DocxReportExporter(),
        new XlsxReportExporter()
    ];

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal) || arg.Length == 2)
            {
                throw new ArgumentException($"Unexpected argument '{arg}'. Use --name value.");
            }

            var key = arg[2..];
            string? value = null;

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            options[key] = value;
        }

        return options;
    }

    private static string Require(Dictionary<string, string?> options, string key)
    {
        if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"--{key} is required.");
        }

        return value;
    }

    private static DeploymentMode ParseMode(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? DeploymentMode.AssessmentOnly
            : Enum.TryParse<DeploymentMode>(value, ignoreCase: true, out var mode)
                ? mode
                : throw new ArgumentException($"Unknown deployment mode '{value}'.");

    private static ReportFormat ParseFormat(string value) =>
        Enum.TryParse<ReportFormat>(value, ignoreCase: true, out var format)
            ? format
            : throw new ArgumentException(
                $"Unknown format '{value}'. Use {string.Join(", ", Enum.GetNames<ReportFormat>())}.");

    private static string SafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "tenant" : cleaned;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"error: unknown command '{command}'.");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            pgc — Purview Governance Copilot headless CLI

            Usage:
              pgc assess --demo [--format json] [--out report.json]
              pgc assess --tenant <id> --client <app-id> --thumbprint <thumb> [--mode AssessmentOnly] [--format pdf] [--out path]
              pgc assess --tenant <id> --client <app-id> --client-secret <secret>   (lab only)
              pgc decision-pack --demo [--prices "E5=57;E3=36"] [--out decision-pack.md] [--by <author>]   (Windows: evidence store is DPAPI-protected)
              pgc provenance --version <v> --files "dist/a.msi;dist/b.msix" [--sbom bom.json] [--output manifest.json] [--commit <sha>]
              pgc verify-audit [--data-dir <path>]
              pgc store-copy --source <sqlite.db> --target "<connection string>" [--target-provider SqlServer|Sqlite]

            Options:
              --format   Html, Json, Csv, Markdown, Pdf, Docx or Xlsx (default Json)
              --mode     AssessmentOnly, GuidedDeployment, OneClickRecommended, Custom, Expert, Greenfield, Brownfield
              --org      Organisation name printed on the report
              --by       Author printed on the report (default: current user)
              --data-dir Evidence directory (default: %LOCALAPPDATA%\PurviewGovernanceCopilot)
              --out      Report path (default: ./purview-<tenant>-<timestamp>.<ext>)

            Live assessment reuses the certificate in CurrentUser\My. Apply and rollback stay
            disabled: this CLI only reads tenant configuration and writes reports.
            """);
    }
}
