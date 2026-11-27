using PurviewCopilot.Application.Auth;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class AdminConsentLinkTests
{
    private const string Tenant = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string Client = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void CommercialLinkTargetsTheTenantAdminConsentEndpoint()
    {
        var link = AdminConsentLink.Build(CloudEnvironment.Commercial, Tenant, Client);

        Assert.Equal(
            $"https://login.microsoftonline.com/{Tenant}/v2.0/adminconsent?client_id={Client}",
            link);
    }

    [Fact]
    public void GccHighUsesTheGovernmentAuthority()
    {
        var link = AdminConsentLink.Build(CloudEnvironment.GccHigh, Tenant, Client);

        Assert.StartsWith("https://login.microsoftonline.us/", link, StringComparison.Ordinal);
        Assert.DoesNotContain("login.microsoftonline.com", link, StringComparison.Ordinal);
    }

    [Fact]
    public void RedirectUriIsEncodedAndOptional()
    {
        var link = AdminConsentLink.Build(CloudEnvironment.Commercial, Tenant, Client, "https://localhost:5001/signin-oidc");

        Assert.Contains("redirect_uri=https%3A%2F%2Flocalhost%3A5001%2Fsignin-oidc", link, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidIdentifiersAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => AdminConsentLink.Build(CloudEnvironment.Commercial, "contoso.onmicrosoft.com", Client));
        Assert.Throws<InvalidOperationException>(() => AdminConsentLink.Build(CloudEnvironment.Commercial, Tenant, "not-a-guid"));
    }
}
