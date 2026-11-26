using System.Reflection;
using System.Runtime.Loader;

namespace PurviewCopilot.Application.Modules;

public sealed record PluginLoadResult(
    IReadOnlyList<IModule> Modules,
    IReadOnlyList<string> Messages);

/// <summary>
/// Discovers external modules from a directory. A plugin is any public, non-abstract class with
/// a parameterless constructor that implements <see cref="IModule"/>; it must declare a matching
/// descriptor and key, and it is keyed as <see cref="PurviewCopilot.Domain.ModuleKey.Custom"/>
/// so it cannot silently impersonate a built-in module.
///
/// Loading executes third-party code. The application points this at the signed-in user's own
/// <c>%LOCALAPPDATA%\PurviewGovernanceCopilot\plugins</c> directory, and each assembly is loaded
/// in isolation so a bad plugin is skipped rather than crashing the assessment.
/// </summary>
public static class PluginModuleLoader
{
    public static PluginLoadResult Load(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return new PluginLoadResult(Array.Empty<IModule>(), new[] { $"Plugin directory {directory} does not exist; no external modules loaded." });
        }

        var modules = new List<IModule>();
        var messages = new List<string>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var context = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(file), isCollectible: false);
                var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(file));
                var loaded = 0;

                foreach (var type in assembly.GetExportedTypes().Where(t =>
                             typeof(IModule).IsAssignableFrom(t) &&
                             t is { IsClass: true, IsAbstract: false } &&
                             t.GetConstructor(Type.EmptyTypes) is not null))
                {
                    if (Activator.CreateInstance(type) is not IModule module)
                    {
                        continue;
                    }

                    if (module.Descriptor is null || module.Descriptor.Key != module.Key)
                    {
                        messages.Add($"{Path.GetFileName(file)}: {type.Name} rejected because its descriptor key does not match its Key.");
                        continue;
                    }

                    modules.Add(module);
                    loaded++;
                }

                messages.Add(loaded > 0
                    ? $"{Path.GetFileName(file)}: loaded {loaded} module(s)."
                    : $"{Path.GetFileName(file)}: no IModule implementation found.");
            }
            catch (Exception ex)
            {
                messages.Add($"{Path.GetFileName(file)}: not loaded — {ex.Message}");
            }
        }

        return new PluginLoadResult(modules, messages);
    }
}
