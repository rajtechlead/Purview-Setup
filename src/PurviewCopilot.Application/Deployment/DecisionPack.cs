using System.Globalization;
using System.Text;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

public sealed record DecisionOption(
    string Key,
    string Title,
    string Summary,
    IReadOnlyList<Recommendation> Items)
{
    public int OperationCount => Items.Sum(i => i.Operations.Count);
    public int IrreversibleCount => Items.SelectMany(i => i.Operations)
        .Count(o => o.Reversibility == Reversibility.Irreversible);
}

public sealed record PhasePlan(string Phase, string Description, int OperationCount);

public sealed record OwnerAssignment(string Module, string Role, int OperationCount);

public sealed record CostLine(string Licence, int Recommendations, decimal? UnitPrice, decimal? Total);

public sealed record DecisionPack(
    string Title,
    IReadOnlyList<DecisionOption> Options,
    IReadOnlyList<PhasePlan> Phases,
    IReadOnlyList<OwnerAssignment> Owners,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<CostLine> Costs,
    IReadOnlyList<string> RollbackLimits,
    string Architecture,
    string Guidance)
{
    public DecisionOption Option(string key) =>
        Options.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown decision option '{key}'.", nameof(key));
}

/// <summary>
/// Builds the pre-change decision pack: three costed scope options derived from the recommendations,
/// who owns each module, the phased rollout, how acceptance is proven, and the limits of rollback.
/// Nothing is invented: costs appear only when a licence price is supplied.
/// </summary>
public static class DecisionPackBuilder
{
    public static DecisionPack Build(
        ChangePlan plan,
        IReadOnlyList<Recommendation> recommendations,
        IReadOnlyDictionary<string, decimal>? licencePrices = null,
        string title = "Purview governance decision pack")
    {
        var deduped = recommendations
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToArray();

        var minimum = deduped.Where(r => r.Priority == Severity.Critical).ToArray();
        var recommended = deduped.Where(r => r.Priority is Severity.Critical or Severity.High or Severity.Medium).ToArray();
        var enhanced = deduped;

        var options = new[]
        {
            new DecisionOption("minimum", "Minimum",
                $"Closes {minimum.Length} critical finding(s) fastest; lower-severity work is deferred.",
                minimum),
            new DecisionOption("recommended", "Recommended",
                $"Covers every critical, high and medium finding ({recommended.Length}); balances risk and effort.",
                recommended),
            new DecisionOption("enhanced", "Enhanced",
                $"Implements all {enhanced.Length} recommendation(s), including low-severity hardening.",
                enhanced)
        };

        var phases = DeploymentPhases.All
            .Select(definition => definition.Phase)
            .Select(phase =>
            {
                var definition = DeploymentPhases.For(phase);
                var count = DeploymentPhases.OperationsFor(plan, phase).Count;
                return new PhasePlan(definition.Name, definition.Description, count);
            })
            .ToArray();

        var owners = plan.Operations
            .GroupBy(o => o.Module)
            .Select(g => new OwnerAssignment(g.Key.ToString(), OwnerFor(g.Key), g.Count()))
            .OrderBy(o => o.Module, StringComparer.Ordinal)
            .ToArray();

        var acceptance = BuildAcceptance(phases, plan);

        var costs = BuildCosts(enhanced, licencePrices);

        var rollback = new List<string>
        {
            $"Reversible operations can be removed again ({plan.Operations.Count(o => o.Reversibility == Reversibility.Reversible)}).",
            $"Restorable operations rely on the pre-phase configuration backup ({plan.Operations.Count(o => o.Reversibility == Reversibility.Restorable)}).",
            plan.IrreversibleOperationCount == 0
                ? "No irreversible operation is in scope."
                : $"Irreversible operations cannot be reconstructed from configuration and need an approved risk exception ({string.Join(", ", plan.IrreversibleOperationIds)}).",
            "Restoring a configuration backup does not restore deleted content, mailbox items or audit history."
        };

        return new DecisionPack(
            title,
            options,
            phases,
            owners,
            acceptance,
            costs,
            rollback,
            "Changes are applied through Purview configuration only; the assessment plane is read-only, " +
            "the deployment runs in phases with a backup per phase, and no user content is read or written.",
            "Start with Minimum when change windows are short, use Recommended for a normal rollout, and choose " +
            "Enhanced when audit readiness, not just risk closure, is the goal.");
    }

    private static IReadOnlyList<string> BuildAcceptance(IReadOnlyList<PhasePlan> phases, ChangePlan plan)
    {
        var criteria = new List<string>
        {
            "Authentication is validated and the tenant fingerprint matches the approved state.",
            $"Every operation in the plan is accounted for ({plan.Operations.Count}).",
            "Each phase passes its post-deployment validation before the next phase starts."
        };

        foreach (var phase in phases.Where(p => p.OperationCount > 0))
        {
            criteria.Add($"{phase.Phase}: {phase.OperationCount} operation(s) applied and validated with no control regression.");
        }

        return criteria;
    }

    private static IReadOnlyList<CostLine> BuildCosts(
        IReadOnlyList<Recommendation> items,
        IReadOnlyDictionary<string, decimal>? prices)
    {
        return items
            .SelectMany(r => r.RequiredLicenses)
            .GroupBy(l => l, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                decimal? unit = prices is not null && prices.TryGetValue(g.Key, out var price) ? price : null;
                return new CostLine(g.Key, g.Count(), unit, unit is null ? null : unit * g.Count());
            })
            .ToArray();
    }

    private static string OwnerFor(ModuleKey module) => module switch
    {
        ModuleKey.Audit or ModuleKey.RecordsManagement or ModuleKey.DataLifecycle
            or ModuleKey.CommunicationCompliance or ModuleKey.EDIscovery => "Compliance Manager",
        ModuleKey.SensitivityLabels or ModuleKey.AutoLabeling or ModuleKey.InformationProtection => "Information Protection Administrator",
        ModuleKey.DataLossPrevention => "Data Security Administrator",
        ModuleKey.EndpointDlp => "Endpoint Security Administrator",
        ModuleKey.InsiderRisk => "Insider Risk Administrator",
        ModuleKey.CopilotGovernance => "AI Governance Lead",
        ModuleKey.ComplianceManager => "Compliance Manager",
        _ => "Purview Administrator"
    };
}

/// <summary>Markdown export of a decision pack, used as the pre-change approval attachment.</summary>
public static class DecisionPackRenderer
{
    public static string RenderMarkdown(DecisionPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var text = new StringBuilder();

        text.AppendLine($"# {pack.Title}").AppendLine();
        text.AppendLine(pack.Guidance).AppendLine();

        text.AppendLine("## Options").AppendLine();
        text.AppendLine("| Option | Findings | Operations | Irreversible | Scope |");
        text.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var option in pack.Options)
        {
            text.AppendLine($"| {option.Title} | {option.Items.Count} | {option.OperationCount} | {option.IrreversibleCount} | {option.Summary} |");
        }

        text.AppendLine().AppendLine("## Architecture").AppendLine();
        text.AppendLine(pack.Architecture);

        text.AppendLine().AppendLine("## Phases").AppendLine();
        foreach (var phase in pack.Phases)
        {
            text.AppendLine($"- **{phase.Phase}** — {phase.OperationCount} operation(s). {phase.Description}");
        }

        text.AppendLine().AppendLine("## Owners").AppendLine();
        foreach (var owner in pack.Owners)
        {
            text.AppendLine($"- {owner.Module}: {owner.Role} ({owner.OperationCount} operation(s))");
        }

        text.AppendLine().AppendLine("## Acceptance criteria").AppendLine();
        foreach (var criterion in pack.AcceptanceCriteria)
        {
            text.AppendLine($"- {criterion}");
        }

        text.AppendLine().AppendLine("## Costs").AppendLine();
        if (pack.Costs.Count == 0)
        {
            text.AppendLine("No additional licence is required by the selected scope.");
        }
        else
        {
            foreach (var cost in pack.Costs)
            {
                var price = cost.UnitPrice is null
                    ? "price not supplied"
                    : string.Create(CultureInfo.InvariantCulture, $"{cost.UnitPrice:0.##} per licence; total {cost.Total:0.##}");
                text.AppendLine($"- {cost.Licence}: {cost.Recommendations} recommendation(s), {price}");
            }
        }

        text.AppendLine().AppendLine("## Rollback limits").AppendLine();
        foreach (var limit in pack.RollbackLimits)
        {
            text.AppendLine($"- {limit}");
        }

        return text.ToString();
    }
}
