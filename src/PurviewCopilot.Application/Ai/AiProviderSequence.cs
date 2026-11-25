using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Ai;

/// <summary>
/// Operator-controlled auto-switch order. Priorities are rewritten to 1..n after every move so
/// the list has no gaps or duplicates and the orchestrator's priority sort matches the UI.
/// </summary>
public static class AiProviderSequence
{
    public static IReadOnlyList<AiProviderDescriptor> Normalize(IEnumerable<AiProviderDescriptor> providers) =>
        providers
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select((p, index) => p with { Priority = index + 1 })
            .ToList();

    /// <summary>Moves one provider by <paramref name="offset"/> places; values outside the list are clamped.</summary>
    public static IReadOnlyList<AiProviderDescriptor> Move(
        IReadOnlyList<AiProviderDescriptor> providers,
        string providerId,
        int offset)
    {
        var ordered = Normalize(providers).ToList();
        var index = ordered.FindIndex(p => string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return ordered;
        }

        var target = Math.Clamp(index + offset, 0, ordered.Count - 1);
        if (target == index)
        {
            return ordered;
        }

        var item = ordered[index];
        ordered.RemoveAt(index);
        ordered.Insert(target, item);

        return ordered.Select((p, i) => p with { Priority = i + 1 }).ToList();
    }
}
