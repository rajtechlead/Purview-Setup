using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class PlatformSupportTests
{
    private static readonly DateOnly ReviewDate = new(2026, 11, 27);

    [Theory]
    [InlineData(17762, "Enterprise", false)]
    [InlineData(17763, "EnterpriseS", true)]
    [InlineData(20348, "ServerStandard", true)]
    [InlineData(26100, "Enterprise", true)]
    [InlineData(26200, "Enterprise", true)]
    [InlineData(28000, "Enterprise", true)]
    public void SupportedBuilds_AreAccepted(int build, string edition, bool supported)
    {
        var assessment = PlatformSupport.Evaluate(new WindowsHost(build, edition, AsOf: ReviewDate));

        Assert.Equal(supported, assessment.IsSupported);
    }

    [Fact]
    public void Windows11_24H2_HomeAndPro_AreRefusedAfterTheirEndOfUpdates()
    {
        var assessment = PlatformSupport.Evaluate(new WindowsHost(26100, "Professional", AsOf: ReviewDate));

        Assert.Equal(PlatformStatus.Unsupported, assessment.Status);
        Assert.Contains("24H2", assessment.Detail);
    }

    [Fact]
    public void Windows11_24H2_Enterprise_IsSupportedUntilItsOwnDate()
    {
        var assessment = PlatformSupport.Evaluate(new WindowsHost(26100, "Enterprise", AsOf: ReviewDate));

        Assert.Equal(PlatformStatus.Supported, assessment.Status);
        Assert.Contains("2027-10-12", assessment.Detail);
    }

    [Fact]
    public void Windows11_23H2_IsRefusedAfter10November2026()
    {
        var assessment = PlatformSupport.Evaluate(new WindowsHost(22631, "Enterprise", AsOf: ReviewDate));

        Assert.Equal(PlatformStatus.Unsupported, assessment.Status);
        Assert.Contains("2026-11-10", assessment.Detail);
    }

    [Fact]
    public void Windows10_Home_IsRefusedBecauseDotNet10DoesNotSupportIt()
    {
        var assessment = PlatformSupport.Evaluate(new WindowsHost(19045, "Core", AsOf: ReviewDate));

        Assert.Equal(PlatformStatus.Unsupported, assessment.Status);
        Assert.Contains("Windows 10 Home or Pro", assessment.Detail);
    }

    [Fact]
    public void Windows10_22H2_Enterprise_WarnsUntilEsuEnrolmentIsConfirmed()
    {
        var assessment = PlatformSupport.Evaluate(new WindowsHost(19045, "Enterprise", EsuEnrolled: false, AsOf: ReviewDate));

        Assert.Equal(PlatformStatus.Warning, assessment.Status);
        Assert.Contains("Extended Security Updates", assessment.Title);
    }

    [Fact]
    public void Windows10_22H2_WithEsu_IsSupportedWhileEsuLasts()
    {
        var assessment = PlatformSupport.Evaluate(new WindowsHost(19045, "Enterprise", EsuEnrolled: true, AsOf: ReviewDate));

        Assert.Equal(PlatformStatus.Supported, assessment.Status);
        Assert.Contains("2028-10-10", assessment.Detail);
    }

    [Fact]
    public void Windows10_22H2_WithEsu_IsRefusedAfterEsuEnds()
    {
        var assessment = PlatformSupport.Evaluate(new WindowsHost(19045, "Enterprise", EsuEnrolled: true, new DateOnly(2028, 10, 11)));

        Assert.Equal(PlatformStatus.Unsupported, assessment.Status);
    }

    [Fact]
    public void Windows10_Ltsc2019_DoesNotRequireEsuBeforeItsOwnEndDate()
    {
        var assessment = PlatformSupport.Evaluate(new WindowsHost(17763, "EnterpriseS", AsOf: ReviewDate));

        Assert.Equal(PlatformStatus.Supported, assessment.Status);
        Assert.Contains("2029-01-10", assessment.Detail);
    }

    [Fact]
    public void WindowsServer2019_IsSupportedToItsExtendedDate()
    {
        var assessment = PlatformSupport.Evaluate(new WindowsHost(17763, "ServerStandard", AsOf: ReviewDate));

        Assert.Equal(PlatformStatus.Supported, assessment.Status);
        Assert.Contains("2029-01-09", assessment.Detail);
    }

    [Fact]
    public void WindowsServer2025_AndWindows11_24H2_AreDisambiguatedByEdition()
    {
        var server = PlatformSupport.Evaluate(new WindowsHost(26100, "ServerDatacenter", AsOf: ReviewDate));
        var client = PlatformSupport.Evaluate(new WindowsHost(26100, "Enterprise", AsOf: ReviewDate));

        Assert.Contains("Server 2025", server.Detail);
        Assert.Contains("Windows 11 24H2", client.Detail);
    }

    [Fact]
    public void UnknownBuild_IsRefusedRatherThanAssumedSupported()
    {
        var assessment = PlatformSupport.Evaluate(new WindowsHost(99999, "Enterprise", AsOf: ReviewDate));

        Assert.Equal(PlatformStatus.Unsupported, assessment.Status);
    }

    [Fact]
    public void ReleaseWithinTheWarningWindow_ProducesAWarning()
    {
        // Windows 10 LTSC 2021 ends 2027-01-13, inside the 90-day warning window.
        var assessment = PlatformSupport.Evaluate(new WindowsHost(19044, "EnterpriseS", AsOf: ReviewDate));

        Assert.Equal(PlatformStatus.Warning, assessment.Status);
        Assert.Contains("2027-01-13", assessment.Detail);
    }

    [Fact]
    public void MatrixEntries_AreUniquePerBuildAndProduct()
    {
        var duplicates = PlatformSupport.Releases
            .GroupBy(r => (r.Build, r.IsServer))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }
}
