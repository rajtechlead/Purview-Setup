using PurviewCopilot.Application.Auth;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class CertificateLifecyclePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);

    private static CertificateInfo Certificate(DateTimeOffset notAfter) =>
        new("THUMB", "CN=Purview Governance Copilot", "CN=Self", Now.AddMonths(-11), notAfter, true, "CurrentUser\\My");

    [Fact]
    public void DefaultLifetimeIsTwelveMonths()
    {
        Assert.Equal(12, CertificateLifecyclePolicy.DefaultValidityMonths);
        Assert.Contains(12, CertificateLifecyclePolicy.SupportedValidityMonths);
    }

    [Theory]
    [InlineData(120, CertificateLifetimeState.Healthy)]
    [InlineData(31, CertificateLifetimeState.Healthy)]
    [InlineData(30, CertificateLifetimeState.Expiring)]
    [InlineData(1, CertificateLifetimeState.Expiring)]
    [InlineData(0, CertificateLifetimeState.Expired)]
    [InlineData(-5, CertificateLifetimeState.Expired)]
    public void LifetimeStateFollowsTheRotationLead(int daysRemaining, CertificateLifetimeState expected)
    {
        Assert.Equal(expected, CertificateLifecyclePolicy.Evaluate(Certificate(Now.AddDays(daysRemaining)), Now));
    }

    [Fact]
    public void RotationIsUrgentInsideTheLeadWindow()
    {
        Assert.False(CertificateLifecyclePolicy.PlanRotation("CN=x", Certificate(Now.AddDays(90)), Now).Required);
        Assert.True(CertificateLifecyclePolicy.PlanRotation("CN=x", Certificate(Now.AddDays(10)), Now).Required);
        Assert.True(CertificateLifecyclePolicy.PlanRotation("CN=x", Certificate(Now.AddDays(-1)), Now).Required);
    }

    [Fact]
    public void RotationKeepsAnOverlapBeforeRetiringTheOldKey()
    {
        var plan = CertificateLifecyclePolicy.PlanRotation("CN=x", Certificate(Now.AddDays(5)), Now);

        Assert.Equal(Now.AddDays(CertificateLifecyclePolicy.MinimumOverlapDays), plan.RetireOldAfterUtc);
        Assert.Contains(plan.Steps, s => s.Contains("additional keyCredential", StringComparison.Ordinal));
        Assert.Contains(plan.Steps, s => s.Contains("Validate authentication", StringComparison.Ordinal));
        Assert.Contains(plan.Steps, s => s.Contains("at least 7 days", StringComparison.Ordinal));

        var retire = Array.FindIndex(plan.Steps.ToArray(), s => s.StartsWith("Retire", StringComparison.Ordinal));
        var add = Array.FindIndex(plan.Steps.ToArray(), s => s.Contains("Upload", StringComparison.Ordinal));
        Assert.True(add < retire, "The replacement must be added before the old key is retired.");
    }

    [Fact]
    public void RetirementIsAnchoredToTheRotationStartNotTheOldExpiry()
    {
        var plan = CertificateLifecyclePolicy.PlanRotation("CN=x", Certificate(Now.AddDays(200)), Now);

        Assert.Equal(Now.AddDays(CertificateLifecyclePolicy.MinimumOverlapDays), plan.RetireOldAfterUtc);
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(12, false)]
    [InlineData(24, false)]
    [InlineData(36, true)]
    [InlineData(120, true)]
    public void SupportedLifetimesValidate(int months, bool acknowledged)
    {
        CertificateLifecyclePolicy.ValidateLifetime(months, acknowledged);
    }

    [Theory]
    [InlineData(36)]
    [InlineData(60)]
    [InlineData(120)]
    public void LongLifetimesRequireAcknowledgement(int months)
    {
        Assert.Throws<InvalidOperationException>(() => CertificateLifecyclePolicy.ValidateLifetime(months, false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(18)]
    [InlineData(121)]
    public void UnsupportedLifetimesAreRejected(int months)
    {
        Assert.Throws<InvalidOperationException>(() => CertificateLifecyclePolicy.ValidateLifetime(months, true));
    }
}
