using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class PlatformSupportTests
{
    [Theory]
    [InlineData(17762, false)]
    [InlineData(17763, true)]
    [InlineData(19045, true)]
    [InlineData(20348, true)]
    [InlineData(26100, true)]
    public void BuildFloor_IsEnforced(int build, bool supported)
    {
        Assert.Equal(supported, PlatformSupport.IsSupported(new Version(10, 0, build)));
    }
}
