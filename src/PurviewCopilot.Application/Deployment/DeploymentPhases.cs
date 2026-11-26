using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

/// <summary>
/// Fixed, ordered production phases. A phase owns the operations for its modules, so only one
/// risk domain changes at a time and every phase has its own approval, backup and validation.
/// </summary>
public static class DeploymentPhases
{
    public static readonly IReadOnlyList<DeploymentPhaseDefinition> All = new[]
    {
        new DeploymentPhaseDefinition(DeploymentPhase.Discovery, "Discovery and readiness",
            "Read-only connectivity, licensing and backup readiness. No changes.",
            Array.Empty<ModuleKey>()),
        new DeploymentPhaseDefinition(DeploymentPhase.MonitoringAndAudit, "Audit and monitoring",
            "Audit configuration, audit retention and reporting controls.",
            new[] { ModuleKey.Audit, ModuleKey.ComplianceManager }),
        new DeploymentPhaseDefinition(DeploymentPhase.InformationProtection, "Information protection",
            "Sensitivity labels, label policies and auto-labelling.",
            new[] { ModuleKey.InformationProtection, ModuleKey.SensitivityLabels, ModuleKey.AutoLabeling, ModuleKey.DataClassification }),
        new DeploymentPhaseDefinition(DeploymentPhase.DataLossPrevention, "Data loss prevention",
            "DLP and endpoint DLP policies.",
            new[] { ModuleKey.DataLossPrevention, ModuleKey.EndpointDlp }),
        new DeploymentPhaseDefinition(DeploymentPhase.RecordsManagement, "Records and retention",
            "Records management, retention labels and lifecycle policies.",
            new[] { ModuleKey.RecordsManagement, ModuleKey.DataLifecycle }),
        new DeploymentPhaseDefinition(DeploymentPhase.InsiderRiskAndAi, "Insider risk, eDiscovery and AI",
            "Insider risk, communication compliance, eDiscovery, DSPM, AI and Copilot governance.",
            new[] { ModuleKey.InsiderRisk, ModuleKey.CommunicationCompliance, ModuleKey.EDIscovery, ModuleKey.Dspm, ModuleKey.AiGovernance, ModuleKey.CopilotGovernance })
    };

    public static DeploymentPhaseDefinition For(DeploymentPhase phase) =>
        All.FirstOrDefault(d => d.Phase == phase)
        ?? throw new ArgumentOutOfRangeException(nameof(phase), "Unknown deployment phase.");

    public static IReadOnlyList<ChangeOperation> OperationsFor(ChangePlan plan, DeploymentPhase phase)
    {
        var definition = For(phase);

        return definition.Modules.Count == 0
            ? Array.Empty<ChangeOperation>()
            : plan.Operations.Where(o => definition.Modules.Contains(o.Module)).ToList();
    }

    public static DeploymentPhase Next(DeploymentPhase phase) => phase switch
    {
        DeploymentPhase.Discovery => DeploymentPhase.MonitoringAndAudit,
        DeploymentPhase.MonitoringAndAudit => DeploymentPhase.InformationProtection,
        DeploymentPhase.InformationProtection => DeploymentPhase.DataLossPrevention,
        DeploymentPhase.DataLossPrevention => DeploymentPhase.RecordsManagement,
        DeploymentPhase.RecordsManagement => DeploymentPhase.InsiderRiskAndAi,
        _ => throw new InvalidOperationException("The final phase has no successor.")
    };
}
