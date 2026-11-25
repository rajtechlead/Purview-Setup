using System.Text.Json;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

/// <summary>Portable JSON envelope for a change plan (configuration-as-code export/import).</summary>
public sealed record ConfigurationPackageDocument(
    string SchemaVersion,
    DateTimeOffset ExportedUtc,
    ChangePlan Plan);

/// <summary>
/// Serialises a reviewed change plan to JSON and reads it back on another workstation. Import
/// assigns a new plan id and timestamp so the original approval evidence stays intact, and always
/// requires simulation and approval again before anything is applied.
/// </summary>
public static class ConfigurationPackage
{
    public const string CurrentSchemaVersion = "1.0";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Export(ChangePlan plan, DateTimeOffset exportedUtc) =>
        JsonSerializer.Serialize(new ConfigurationPackageDocument(CurrentSchemaVersion, exportedUtc, plan), Json);

    public static ChangePlan Import(string json, DateTimeOffset importedUtc)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("The configuration package is empty.");
        }

        ChangePlan? plan;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement.TryGetProperty("plan", out var inner) ? inner : document.RootElement;
            plan = root.Deserialize<ChangePlan>(Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The configuration package is not valid JSON.", ex);
        }

        if (plan is null)
        {
            throw new InvalidOperationException("The configuration package does not contain a change plan.");
        }

        if (plan.Operations is null || plan.Operations.Count == 0)
        {
            throw new InvalidOperationException("The imported plan has no operations.");
        }

        return plan with
        {
            Id = Guid.NewGuid(),
            CreatedUtc = importedUtc,
            Title = $"{plan.Title} (imported)"
        };
    }
}
