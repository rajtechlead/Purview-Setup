namespace PurviewCopilot.Domain;

public enum PlatformStatus
{
    Supported = 0,
    Warning,
    Unsupported
}

/// <summary>Result of checking a host against the lifecycle matrix. Unsupported is fatal.</summary>
public sealed record PlatformAssessment(PlatformStatus Status, string Title, string Detail)
{
    public bool IsSupported => Status != PlatformStatus.Unsupported;
}

/// <summary>
/// The facts needed to judge a host. Edition is the registry `EditionID` value, which is
/// optional so the check is usable from tests and headless callers.
/// </summary>
public sealed record WindowsHost(int Build, string? EditionId = null, bool EsuEnrolled = false, DateOnly? AsOf = null);

/// <summary>
/// One Windows release in the matrix. Dates come from Microsoft lifecycle and release-health
/// pages and are the end of security updates for that servicing branch.
/// </summary>
public sealed record PlatformRelease(
    string Name,
    int Build,
    DateOnly HomeProEndOfUpdates,
    DateOnly EnterpriseEndOfUpdates,
    bool IsServer = false,
    bool IsWindows10 = false,
    bool EnterpriseOnly = false);

/// <summary>
/// Platform floor and lifecycle gate. The application supports only Windows releases that are
/// both inside Microsoft support and supported by the .NET 10 desktop runtime, so an
/// end-of-life host is refused before any tenant data is opened. Dates reviewed 2026-11-27.
/// </summary>
public static class PlatformSupport
{
    /// <summary>Windows 10 1809 / Windows Server 2019 is the oldest build in the matrix.</summary>
    public const int MinimumBuild = 17763;

    public const int EndOfLifeWarningDays = 90;

    public static readonly DateOnly Windows10GeneralSupportEnd = new(2025, 10, 14);
    public static readonly DateOnly Windows10EsuEnd = new(2028, 10, 10);

    /// <summary>
    /// Matrix source of truth. Keep in sync with
    /// docs/project/trackers/06-platform-support.csv and Microsoft's release-health pages.
    /// </summary>
    public static IReadOnlyList<PlatformRelease> Releases { get; } = new[]
    {
        new PlatformRelease("Windows Server 2019 (1809)", 17763, new(2029, 1, 9), new(2029, 1, 9), IsServer: true),
        new PlatformRelease("Windows 10 Enterprise LTSC 2019", 17763, new(2029, 1, 10), new(2029, 1, 10), IsWindows10: true, EnterpriseOnly: true),
        new PlatformRelease("Windows Server 2022", 20348, new(2031, 10, 14), new(2031, 10, 14), IsServer: true),
        new PlatformRelease("Windows 11 23H2", 22631, new(2026, 11, 10), new(2026, 11, 10)),
        new PlatformRelease("Windows 11 24H2", 26100, new(2026, 10, 13), new(2027, 10, 12)),
        new PlatformRelease("Windows Server 2025", 26100, new(2034, 11, 14), new(2034, 11, 14), IsServer: true),
        new PlatformRelease("Windows 11 25H2", 26200, new(2027, 10, 12), new(2028, 10, 10)),
        new PlatformRelease("Windows 11 26H2", 26300, new(2028, 10, 10), new(2029, 10, 9)),
        new PlatformRelease("Windows 11 26H1", 28000, new(2028, 3, 14), new(2029, 3, 13)),
        new PlatformRelease("Windows 10 Enterprise LTSC 2021 (21H2)", 19044, new(2027, 1, 13), new(2027, 1, 13), IsWindows10: true, EnterpriseOnly: true),
        new PlatformRelease("Windows 10 22H2", 19045, new(2025, 10, 14), new(2025, 10, 14), IsWindows10: true, EnterpriseOnly: true)
    };

    public static PlatformAssessment Evaluate(WindowsHost host)
    {
        var asOf = host.AsOf ?? DateOnly.FromDateTime(DateTime.UtcNow);

        if (host.Build < MinimumBuild)
        {
            return new PlatformAssessment(
                PlatformStatus.Unsupported,
                "Unsupported Windows build",
                $"Windows build {host.Build} is below the minimum ({MinimumBuild}). {Describe()}");
        }

        var isServer = IsServerEdition(host.EditionId);
        var candidates = Releases
            .Where(r => r.Build == host.Build && r.IsServer == isServer)
            .ToList();

        if (candidates.Count == 0)
        {
            return new PlatformAssessment(
                PlatformStatus.Unsupported,
                "Unsupported Windows release",
                $"Windows build {host.Build} is not in the supported release matrix. {Describe()}");
        }

        var release = candidates.OrderByDescending(r => r.EnterpriseEndOfUpdates).First();
        var isEnterprise = IsEnterpriseLikeEdition(host.EditionId);

        if (release.EnterpriseOnly && host.EditionId is not null && !isEnterprise)
        {
            return new PlatformAssessment(
                PlatformStatus.Unsupported,
                "Unsupported Windows edition",
                $"{release.Name} requires an Enterprise, Education or LTSC edition. " +
                ".NET 10 does not support Windows 10 Home or Pro, and Windows 10 general support ended " +
                $"{Windows10GeneralSupportEnd:yyyy-MM-dd}.");
        }

        // Only Windows 10 22H2 (19045) depends on ESU. LTSC releases carry their own support dates.
        if (release.IsWindows10 && release.Build == 19045 && !isServer)
        {
            if (!isEnterprise && host.EditionId is not null)
            {
                return new PlatformAssessment(
                    PlatformStatus.Unsupported,
                    "Unsupported Windows 10 edition",
                    "Windows 10 Home and Pro reached end of support on 2025-10-14 and are not supported by .NET 10.");
            }

            if (asOf > Windows10EsuEnd)
            {
                return new PlatformAssessment(
                    PlatformStatus.Unsupported,
                    "Windows 10 Extended Security Updates ended",
                    $"Windows 10 ESU ends {Windows10EsuEnd:yyyy-MM-dd}. Upgrade the host to Windows 11 or Windows Server.");
            }

            if (!host.EsuEnrolled)
            {
                return new PlatformAssessment(
                    PlatformStatus.Warning,
                    "Windows 10 requires active Extended Security Updates",
                    $"Windows 10 general support ended {Windows10GeneralSupportEnd:yyyy-MM-dd}. This host is only " +
                    $"supported while ESU is active (to {Windows10EsuEnd:yyyy-MM-dd}); enrolment cannot be verified locally.");
            }
        }

        var end = release.Build == 19045
            ? Windows10EsuEnd
            : isEnterprise || host.EditionId is null
                ? release.EnterpriseEndOfUpdates
                : release.HomeProEndOfUpdates;

        if (asOf > end)
        {
            return new PlatformAssessment(
                PlatformStatus.Unsupported,
                "Windows release is out of support",
                $"{release.Name} stopped receiving security updates on {end:yyyy-MM-dd}. " +
                "Upgrade before running the copilot against a tenant.");
        }

        if (end.DayNumber - asOf.DayNumber <= EndOfLifeWarningDays)
        {
            return new PlatformAssessment(
                PlatformStatus.Warning,
                "Windows release is approaching end of support",
                $"{release.Name} stops receiving security updates on {end:yyyy-MM-dd}. Plan the upgrade.");
        }

        if (host.EditionId is null)
        {
            return new PlatformAssessment(
                PlatformStatus.Warning,
                "Windows edition could not be determined",
                $"{release.Name} is supported to {end:yyyy-MM-dd}; confirm the edition is Enterprise, Education, " +
                "Pro or Server as appropriate, because support dates differ by edition.");
        }

        return new PlatformAssessment(
            PlatformStatus.Supported,
            "Supported platform",
            $"{release.Name} ({release.Build}) is supported to {end:yyyy-MM-dd}.");
    }

    /// <summary>Compatibility overload for callers that only know the build. Edition is treated as unknown.</summary>
    public static bool IsSupported(Version version) => Evaluate(new WindowsHost(version.Build)).IsSupported;

    public static bool IsServerEdition(string? editionId) =>
        editionId?.Contains("Server", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsEnterpriseLikeEdition(string? editionId) =>
        editionId is not null &&
        (editionId.Contains("Enterprise", StringComparison.OrdinalIgnoreCase) ||
         editionId.Contains("Education", StringComparison.OrdinalIgnoreCase) ||
         editionId.Contains("Server", StringComparison.OrdinalIgnoreCase));

    public static string Describe() =>
        "Windows 11 25H2/26H1/26H2, Windows 11 24H2 Enterprise or Education, Windows Server 2019/2022/2025, " +
        "or Windows 10 Enterprise LTSC with Extended Security Updates. " +
        "End-of-life releases are refused. See docs/project/trackers/06-platform-support.csv.";
}
