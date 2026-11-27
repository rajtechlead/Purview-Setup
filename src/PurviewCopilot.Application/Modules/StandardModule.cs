using System.Text.Json;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Modules;

public sealed record ModuleDescriptor(
    ModuleKey Key,
    string DisplayName,
    string Description,
    IReadOnlyList<string> RequiredLicenses,
    IReadOnlyList<string> Frameworks);

public interface IModule
{
    ModuleDescriptor Descriptor { get; }
    ModuleKey Key { get; }
    IReadOnlyList<ControlRule> Rules { get; }
    Task<IReadOnlyList<Finding>> AssessAsync(TenantSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Recommendation>> RecommendAsync(TenantSnapshot snapshot, IReadOnlyList<Finding> findings, CancellationToken cancellationToken = default);
}

/// <summary>
/// One implementation serves every module; behaviour comes from the rule data. Adding a
/// capability is a catalog entry, not a new class, which keeps the code DRY without
/// preventing a module from overriding this base later (plugin architecture).
/// </summary>
public sealed class StandardModule : IModule
{
    public StandardModule(ModuleDescriptor descriptor, IReadOnlyList<ControlRule> rules)
    {
        Descriptor = descriptor;
        Rules = rules;
    }

    public ModuleDescriptor Descriptor { get; }
    public ModuleKey Key => Descriptor.Key;
    public IReadOnlyList<ControlRule> Rules { get; }

    public Task<IReadOnlyList<Finding>> AssessAsync(TenantSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var findings = Rules.Select(rule => rule.ToFinding(snapshot)).ToList();
        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    public Task<IReadOnlyList<Recommendation>> RecommendAsync(
        TenantSnapshot snapshot,
        IReadOnlyList<Finding> findings,
        CancellationToken cancellationToken = default)
    {
        var recommendations = new List<Recommendation>();

        foreach (var finding in findings.Where(f => f.Status is FindingStatus.Fail or FindingStatus.Warning))
        {
            var rule = Rules.FirstOrDefault(r => r.Id == finding.Id);
            if (rule is null) continue;

            var operation = new ChangeOperation(
                Id: $"OP-{finding.Id}",
                Module: finding.Module,
                Action: finding.Status == FindingStatus.Fail ? OperationAction.Create : OperationAction.Update,
                Target: $"Purview/{finding.Module}/{finding.Id}",
                PayloadJson: JsonSerializer.Serialize(new
                {
                    module = finding.Module.ToString(),
                    control = finding.Id,
                    baseline = "recommended",
                    currentStatus = finding.Status.ToString(),
                    tenant = snapshot.TenantId
                }),
                IsDestructive: false,
                RequiresSimulation: true,
                // A new policy can be removed again; an update overwrites existing configuration and
                // relies on the pre-phase backup to be restored.
                Reversibility: finding.Status == FindingStatus.Fail
                    ? Reversibility.Reversible
                    : Reversibility.Restorable);

            recommendations.Add(new Recommendation(
                Id: $"REC-{finding.Id}",
                Module: finding.Module,
                Title: $"Remediate {finding.Id}: {finding.Title}",
                Rationale: finding.Evidence,
                Priority: finding.Severity,
                Effort: finding.Severity >= Severity.High ? "Medium" : "Low",
                ChangeRisk: finding.Severity >= Severity.High ? "Moderate — pilot and simulate first" : "Low",
                RequiredLicenses: rule.RequiredLicenses,
                Operations: new[] { operation }));
        }

        return Task.FromResult<IReadOnlyList<Recommendation>>(recommendations);
    }
}

/// <summary>Module registry: the single place that knows which modules exist and how they are weighted.</summary>
public sealed class ModuleRegistry
{
    public ModuleRegistry(IEnumerable<IModule>? plugins = null)
    {
        var descriptors = new[]
        {
            new ModuleDescriptor(ModuleKey.InformationProtection, "Information Protection",
                "Label taxonomy, protection settings and encryption.", new[] { "E3" }, new[] { "ISO 27001", "GDPR" }),
            new ModuleDescriptor(ModuleKey.SensitivityLabels, "Sensitivity Labels",
                "Label design, publishing and user experience.", new[] { "E3" }, new[] { "ISO 27001" }),
            new ModuleDescriptor(ModuleKey.AutoLabeling, "Auto Labelling",
                "Service-side and client-side automatic classification.", new[] { "E5" }, new[] { "NIST", "GDPR" }),
            new ModuleDescriptor(ModuleKey.DataLossPrevention, "Data Loss Prevention",
                "Policy design, simulation and enforcement across workloads.", new[] { "E3" }, new[] { "PCI-DSS", "GDPR", "HIPAA" }),
            new ModuleDescriptor(ModuleKey.EndpointDlp, "Endpoint DLP",
                "Device onboarding and endpoint egress controls.", new[] { "E5" }, new[] { "CIS", "ISO 27001" }),
            new ModuleDescriptor(ModuleKey.DataClassification, "Data Classification",
                "Sensitive information types and trainable classifiers.", new[] { "E5" }, new[] { "NIST", "SOC 2" }),
            new ModuleDescriptor(ModuleKey.RecordsManagement, "Records Management",
                "File plan, retention labels and disposition.", new[] { "E5" }, new[] { "ISO 27001", "SOC 2" }),
            new ModuleDescriptor(ModuleKey.DataLifecycle, "Data Lifecycle",
                "Retention policies across workloads.", new[] { "E3" }, new[] { "GDPR", "SOC 2" }),
            new ModuleDescriptor(ModuleKey.Audit, "Audit",
                "Audit ingestion, retention and alerting.", new[] { "E3" }, new[] { "ISO 27001", "SOC 2" }),
            new ModuleDescriptor(ModuleKey.InsiderRisk, "Insider Risk",
                "Insider risk policies, indicators and adaptive protection.", new[] { "E5" }, new[] { "ISO 27001", "SOC 2" }),
            new ModuleDescriptor(ModuleKey.CommunicationCompliance, "Communication Compliance",
                "Supervisory review of communications.", new[] { "E5" }, new[] { "SOC 2", "HIPAA" }),
            new ModuleDescriptor(ModuleKey.EDIscovery, "eDiscovery",
                "Case management, holds and export.", new[] { "E3" }, new[] { "SOC 2" }),
            new ModuleDescriptor(ModuleKey.Dspm, "DSPM",
                "Data security posture and AI visibility.", new[] { "E5" }, new[] { "NIST", "ISO 42001" }),
            new ModuleDescriptor(ModuleKey.ComplianceManager, "Compliance Manager",
                "Compliance posture, identity controls and improvement actions.", new[] { "E3" }, new[] { "ISO 27001", "SOC 2" }),
            new ModuleDescriptor(ModuleKey.AiGovernance, "AI Governance",
                "AI usage policy, prompt protection and AI inventory.", new[] { "E5" }, new[] { "ISO 42001", "NIST AI RMF", "OWASP LLM" }),
            new ModuleDescriptor(ModuleKey.CopilotGovernance, "Microsoft 365 Copilot Governance",
                "Readiness controls for Copilot rollout.", new[] { "Microsoft 365 Copilot" }, new[] { "ISO 42001", "NIST AI RMF" })
        };

        var modules = descriptors
            .Select(d => (IModule)new StandardModule(d, ModuleCatalog.All.Where(r => r.Module == d.Key).ToList()))
            .ToList();

        foreach (var plugin in plugins ?? Array.Empty<IModule>())
        {
            if (plugin is null || modules.Any(m => m.Key == plugin.Key && plugin.Key != ModuleKey.Custom))
            {
                continue;
            }

            modules.Add(plugin);
        }

        Modules = modules;

        Weights = new Dictionary<ModuleKey, double>
        {
            [ModuleKey.Audit] = 1.5,
            [ModuleKey.DataLossPrevention] = 1.5,
            [ModuleKey.InformationProtection] = 1.3,
            [ModuleKey.EndpointDlp] = 1.2,
            [ModuleKey.InsiderRisk] = 1.2,
            [ModuleKey.AiGovernance] = 1.2,
            [ModuleKey.CopilotGovernance] = 1.1,
            [ModuleKey.Custom] = 1.0
        };
    }

    public IReadOnlyList<IModule> Modules { get; }
    public IReadOnlyDictionary<ModuleKey, double> Weights { get; }

    public IModule Get(ModuleKey key) => Modules.First(m => m.Key == key);
}
