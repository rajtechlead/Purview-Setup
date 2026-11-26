namespace PurviewCopilot.Domain;

/// <summary>
/// Supported platform floor: Windows 10 1809 (build 17763), Windows 11 and Windows Server
/// 2019/2022/2025. Older, end-of-life builds are refused before any data is opened.
/// </summary>
public static class PlatformSupport
{
    public const int MinimumBuild = 17763;

    public static bool IsSupported(Version version) => version.Build >= MinimumBuild;

    public static string Describe() =>
        "Windows 10 version 1809 or later, Windows 11, or Windows Server 2019/2022/2025. " +
        "End-of-life operating systems are not supported.";
}
