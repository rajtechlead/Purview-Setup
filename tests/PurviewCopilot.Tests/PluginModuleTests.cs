using PurviewCopilot.Application.Modules;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class PluginModuleTests
{
    [Fact]
    public void CustomPlugin_IsRegisteredAlongsideBuiltIns()
    {
        var registry = new ModuleRegistry(new[] { new FakePlugin(ModuleKey.Custom) });

        Assert.Contains(registry.Modules, m => m.Descriptor.DisplayName == "Fake plugin");
        Assert.Equal(17, registry.Modules.Count);
        Assert.Equal(1.0, registry.Weights[ModuleKey.Custom]);
    }

    [Fact]
    public void PluginCannotImpersonateABuiltInModule()
    {
        var registry = new ModuleRegistry(new[] { new FakePlugin(ModuleKey.Audit) });

        Assert.DoesNotContain(registry.Modules, m => m.Descriptor.DisplayName == "Fake plugin");
        Assert.Single(registry.Modules, m => m.Key == ModuleKey.Audit);
    }

    [Fact]
    public void MissingPluginDirectory_IsReportedAndDoesNotThrow()
    {
        var result = PluginModuleLoader.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));

        Assert.Empty(result.Modules);
        Assert.Contains(result.Messages, m => m.Contains("does not exist", StringComparison.Ordinal));
    }

    private sealed class FakePlugin : IModule
    {
        public FakePlugin(ModuleKey key)
        {
            Key = key;
            Descriptor = new ModuleDescriptor(key, "Fake plugin", "Test extension", Array.Empty<string>(), Array.Empty<string>());
        }

        public ModuleDescriptor Descriptor { get; }
        public ModuleKey Key { get; }
        public IReadOnlyList<ControlRule> Rules { get; } = Array.Empty<ControlRule>();

        public Task<IReadOnlyList<Finding>> AssessAsync(TenantSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Finding>>(Array.Empty<Finding>());

        public Task<IReadOnlyList<Recommendation>> RecommendAsync(TenantSnapshot snapshot, IReadOnlyList<Finding> findings, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Recommendation>>(Array.Empty<Recommendation>());
    }
}
