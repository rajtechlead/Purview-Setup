using System.Text;
using System.Text.Json;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

public sealed record RestoreStep(string OperationId, string Target, Reversibility Reversibility, string Instruction);

public sealed record RestorePlan(
    bool Ready,
    IReadOnlyList<ValidationCheck> Checks,
    IReadOnlyList<RestoreStep> Steps)
{
    public string Summary => Ready
        ? $"Restore plan ready ({Steps.Count} step(s))."
        : $"Restore is not ready: {string.Join("; ", Checks.Where(c => !c.Passed).Select(c => c.Name))}.";
}

/// <summary>
/// Turns a stored backup back into an executable restore: it proves the backup belongs to the plan,
/// understands its schema, and only claims readiness when every operation can actually be reversed.
/// Irreversible work and missing object exports are reported, never papered over.
/// </summary>
public static class RollbackPlanner
{
    private static readonly string[] SupportedSchemas = { "1.0", "2.0" };

    public static RestorePlan Build(ConfigurationBackup backup, ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(plan);

        var checks = new List<ValidationCheck>
        {
            new("Backup belongs to the plan's tenant",
                string.Equals(backup.TenantId, plan.TenantId, StringComparison.OrdinalIgnoreCase),
                $"{backup.TenantId} → {plan.TenantId}"),
            new("Backup schema is supported",
                SupportedSchemas.Contains(backup.SchemaVersion),
                backup.SchemaVersion)
        };

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(backup.PayloadJson);
        }
        catch (JsonException)
        {
            checks.Add(new ValidationCheck("Backup payload is valid JSON", false, "The payload could not be parsed."));
            return new RestorePlan(false, checks, Array.Empty<RestoreStep>());
        }

        using (document)
        {
            var root = document.RootElement;
            var hasObjects = root.TryGetProperty("objects", out var objects) &&
                             objects.ValueKind is JsonValueKind.Array or JsonValueKind.Object;
            var operations = ReadOperations(root);

            checks.Add(new ValidationCheck("Backup contains the phase operations",
                operations.Count > 0, $"{operations.Count} operation(s)"));
            checks.Add(new ValidationCheck("Backup contains a configuration snapshot",
                root.TryGetProperty("snapshot", out _), "snapshot"));

            var covered = operations.Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var uncovered = plan.Operations.Where(o => !covered.Contains(o.Id)).Select(o => o.Id).ToArray();
            checks.Add(new ValidationCheck("Every plan operation has backup coverage",
                uncovered.Length == 0,
                uncovered.Length == 0 ? "all covered" : $"missing: {string.Join(", ", uncovered)}"));

            var irreversible = plan.Operations
                .Where(o => o.Reversibility == Reversibility.Irreversible)
                .Select(o => o.Id)
                .ToArray();
            checks.Add(new ValidationCheck("No irreversible operation is in scope",
                irreversible.Length == 0,
                irreversible.Length == 0 ? "none" : $"cannot be restored: {string.Join(", ", irreversible)}"));

            var steps = operations.Select(o => new RestoreStep(
                    o.Id,
                    o.Target,
                    o.Reversibility,
                    Instruction(o, hasObjects)))
                .ToArray();

            if (operations.Count == 0)
            {
                steps = plan.Operations.Select(o => new RestoreStep(
                        o.Id, o.Target, o.Reversibility, Instruction(o, hasObjects)))
                    .ToArray();
            }

            var deletedWithoutExport = steps.Any(s =>
                s.Instruction.Contains("object export", StringComparison.Ordinal) && !hasObjects);
            checks.Add(new ValidationCheck("Deleted objects can be recreated from the export",
                !deletedWithoutExport,
                deletedWithoutExport ? "the export has no objects" : "ok"));

            return new RestorePlan(checks.All(c => c.Passed), checks, steps);
        }
    }

    public static string BuildScript(ChangePlan plan, RestorePlan restorePlan)
    {
        var script = new StringBuilder();
        script.AppendLine("# Purview Governance Copilot — restore from backup");
        script.AppendLine("# Review every step. Restoring configuration does not restore deleted content or audit history.");
        script.AppendLine("Connect-IPPSSession -AppId $env:PGC_APP_ID -CertificateThumbprint $env:PGC_THUMBPRINT -Organization $env:PGC_ORG");
        script.AppendLine();

        foreach (var step in restorePlan.Steps)
        {
            script.AppendLine($"# {step.OperationId} ({step.Reversibility}) {step.Target}");
            script.AppendLine($"# {step.Instruction}");
        }

        return script.ToString();
    }

    private static List<ChangeOperation> ReadOperations(JsonElement root)
    {
        if (!root.TryGetProperty("operations", out var operations) || operations.ValueKind != JsonValueKind.Array)
        {
            return new List<ChangeOperation>();
        }

        try
        {
            return operations.Deserialize<List<ChangeOperation>>() ?? new List<ChangeOperation>();
        }
        catch (JsonException)
        {
            return new List<ChangeOperation>();
        }
    }

    private static string Instruction(ChangeOperation operation, bool hasObjects) => operation.Action switch
    {
        OperationAction.Create => $"Remove {operation.Target} if it exists; it did not exist before the backup.",
        OperationAction.Delete when hasObjects => $"Recreate {operation.Target} from the object export.",
        OperationAction.Delete => $"Recreate {operation.Target} from the object export; none was captured, so this needs manual restoration.",
        _ => $"Restore {operation.Target} from the snapshot stored in the backup payload."
    };
}
