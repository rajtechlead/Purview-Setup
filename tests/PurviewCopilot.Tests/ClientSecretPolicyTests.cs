using PurviewCopilot.Application.Auth;
using Xunit;

namespace PurviewCopilot.Tests;

public class ClientSecretPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LabSecretsDefaultToTwelveMonthsWithinEntrasCap()
    {
        Assert.Equal(12, ClientSecretPolicy.DefaultLifetimeMonths);
        Assert.Equal(24, ClientSecretPolicy.MaxLifetimeMonths);
        Assert.Equal(ClientSecretPolicy.SupportedMonths.Max(), ClientSecretPolicy.MaxLifetimeMonths);
    }

    [Theory]
    [InlineData(30, CredentialLifetimeState.Healthy)]
    [InlineData(14, CredentialLifetimeState.Expiring)]
    [InlineData(1, CredentialLifetimeState.Expiring)]
    [InlineData(0, CredentialLifetimeState.Expired)]
    [InlineData(-1, CredentialLifetimeState.Expired)]
    public void LifetimeStateFollowsTheRotationLead(int days, CredentialLifetimeState expected)
    {
        Assert.Equal(expected, ClientSecretPolicy.Evaluate(Now.AddDays(days), Now));
    }

    [Fact]
    public void MissingExpiryIsTreatedAsExpired()
    {
        Assert.Equal(CredentialLifetimeState.Expired, ClientSecretPolicy.Evaluate(null, Now));
        Assert.True(ClientSecretPolicy.NeedsRotation(null, Now));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(24)]
    public void SupportedLifetimesValidate(int months)
    {
        ClientSecretPolicy.ValidateLifetime(months);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(36)]
    [InlineData(120)]
    public void LifetimesOutsideEntrasRangeAreRejected(int months)
    {
        Assert.Throws<InvalidOperationException>(() => ClientSecretPolicy.ValidateLifetime(months));
    }
}
