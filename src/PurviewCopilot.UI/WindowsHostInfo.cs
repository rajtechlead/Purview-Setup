using Microsoft.Win32;
using PurviewCopilot.Domain;

namespace PurviewCopilot.UI;

/// <summary>
/// Reads the host facts the lifecycle gate needs. Edition comes from the registry because
/// Environment.OSVersion does not expose it; the check degrades to the build only if the read fails.
/// </summary>
internal static class WindowsHostInfo
{
    public static WindowsHost Read()
    {
        var build = Environment.OSVersion.Version.Build;
        string? edition = null;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key?.GetValue("CurrentBuildNumber") is string currentBuild && int.TryParse(currentBuild, out var parsed))
            {
                build = parsed;
            }

            edition = key?.GetValue("EditionID") as string;
        }
        catch (Exception)
        {
            // A restricted or redirected registry still leaves a usable build number.
        }

        return new WindowsHost(build, edition);
    }
}
