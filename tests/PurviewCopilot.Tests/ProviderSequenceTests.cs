using PurviewCopilot.Application.Ai;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class ProviderSequenceTests
{
    private static AiProviderDescriptor Provider(string id, int priority, string? displayName = null) =>
        new(id, displayName ?? id.ToUpperInvariant(), AiProviderKind.OpenAi,
            "https://api.openai.com/v1", "gpt", "OPENAI_API_KEY", false, true, priority, 0.01, 0.03, 128_000);

    private static string[] Order(IEnumerable<AiProviderDescriptor> providers) =>
        providers.Select(p => p.Id).ToArray();

    [Fact]
    public void MoveDown_SwapsWithTheNextProvider_AndRenumbers()
    {
        var providers = new[] { Provider("a", 1), Provider("b", 2), Provider("c", 3) };

        var moved = AiProviderSequence.Move(providers, "a", 1);

        Assert.Equal(new[] { "b", "a", "c" }, Order(moved));
        Assert.Equal(new[] { 1, 2, 3 }, moved.Select(p => p.Priority));
    }

    [Fact]
    public void MoveUp_SwapsWithThePreviousProvider()
    {
        var providers = new[] { Provider("a", 1), Provider("b", 2), Provider("c", 3) };

        var moved = AiProviderSequence.Move(providers, "c", -1);

        Assert.Equal(new[] { "a", "c", "b" }, Order(moved));
        Assert.Equal(new[] { 1, 2, 3 }, moved.Select(p => p.Priority));
    }

    [Fact]
    public void MoveAtTheEdges_IsClampedAndLeavesOrderUnchanged()
    {
        var providers = new[] { Provider("a", 1), Provider("b", 2) };

        Assert.Equal(new[] { "a", "b" }, Order(AiProviderSequence.Move(providers, "a", -1)));
        Assert.Equal(new[] { "a", "b" }, Order(AiProviderSequence.Move(providers, "b", 1)));
    }

    [Fact]
    public void DuplicatePriorities_AreNormalisedByNameAndRenumbered()
    {
        var providers = new[]
        {
            Provider("zeta", 1, "Zeta"),
            Provider("alpha", 1, "Alpha"),
            Provider("mid", 5, "Mid")
        };

        var normalized = AiProviderSequence.Normalize(providers);

        Assert.Equal(new[] { "alpha", "zeta", "mid" }, Order(normalized));
        Assert.Equal(new[] { 1, 2, 3 }, normalized.Select(p => p.Priority));
    }

    [Fact]
    public void UnknownProvider_LeavesTheSequenceIntact()
    {
        var providers = new[] { Provider("a", 1), Provider("b", 2) };

        var moved = AiProviderSequence.Move(providers, "missing", 1);

        Assert.Equal(new[] { "a", "b" }, Order(moved));
    }
}
