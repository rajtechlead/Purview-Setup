using PurviewCopilot.Application.Licensing;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class LicensePriceTests
{
    [Fact]
    public void UnusedSeats_UseTheConfiguredPrice()
    {
        var snapshot = new TenantSnapshot
        {
            Licenses = new[] { new LicenseSku("SPE_E5", 5, 15) }
        };

        var insight = LicenseOptimizer.Analyze(snapshot, new Dictionary<string, double> { ["SPE_E5"] = 50 })
            .Single(i => i.Id.StartsWith("LIC-UNUSED", StringComparison.Ordinal));

        Assert.Equal(500, insight.MonthlyOpportunityUsd);
        Assert.Equal("500.00", insight.MonthlyOpportunityUsd.ToString("0.00"));
    }

    [Fact]
    public void OverAssignment_UsesTheConfiguredPrice()
    {
        var snapshot = new TenantSnapshot
        {
            Licenses = new[] { new LicenseSku("SPE_E3", 12, 10) }
        };

        var insight = LicenseOptimizer.Analyze(snapshot, new Dictionary<string, double> { ["SPE_E3"] = 30 })
            .Single(i => i.Id.StartsWith("LIC-OVER", StringComparison.Ordinal));

        Assert.Equal(60, insight.MonthlyOpportunityUsd);
    }

    [Fact]
    public void NoPriceList_LeavesOpportunityAtZero()
    {
        var snapshot = new TenantSnapshot
        {
            Licenses = new[] { new LicenseSku("SPE_E5", 5, 15) }
        };

        Assert.All(LicenseOptimizer.Analyze(snapshot), i => Assert.Equal(0, i.MonthlyOpportunityUsd));
    }
}
