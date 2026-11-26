using PurviewCopilot.Application.Auth;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class CloudProfileTests
{
    [Fact]
    public void CommercialAndGcc_UseWorldwideEndpoints()
    {
        var commercial = CloudProfiles.For(CloudEnvironment.Commercial);
        var gcc = CloudProfiles.For(CloudEnvironment.Gcc);

        Assert.Equal("https://login.microsoftonline.com/", commercial.AuthorityHost.ToString());
        Assert.Equal("https://graph.microsoft.com/v1.0", commercial.GraphBaseUrl);
        Assert.Equal("https://graph.microsoft.com/.default", commercial.GraphScope);
        Assert.Null(commercial.ComplianceConnectionUri);

        Assert.Equal(commercial.GraphBaseUrl, gcc.GraphBaseUrl);
        Assert.Equal(commercial.AuthorityHost, gcc.AuthorityHost);
    }

    [Fact]
    public void GccHigh_UsesUsAuthorityGraphAndCompliance()
    {
        var cloud = CloudProfiles.For(CloudEnvironment.GccHigh);

        Assert.Equal("https://login.microsoftonline.us/", cloud.AuthorityHost.ToString());
        Assert.Equal("https://graph.microsoft.us/v1.0", cloud.GraphBaseUrl);
        Assert.Equal("https://graph.microsoft.us/.default", cloud.GraphScope);
        Assert.Equal("https://ps.compliance.protection.office365.us/powershell-liveid/", cloud.ComplianceConnectionUri);
        Assert.Equal("https://login.microsoftonline.us/organizations", cloud.ComplianceAuthorizationUri);
    }

    [Fact]
    public void Dod_UsesDodGraphAndL5Compliance()
    {
        var cloud = CloudProfiles.For(CloudEnvironment.Dod);

        Assert.Equal("https://dod-graph.microsoft.us/v1.0", cloud.GraphBaseUrl);
        Assert.Equal("https://dod-graph.microsoft.us/.default", cloud.GraphScope);
        Assert.Equal("https://l5.ps.compliance.protection.office365.us/powershell-liveid/", cloud.ComplianceConnectionUri);
        Assert.Equal("https://login.microsoftonline.us/organizations", cloud.ComplianceAuthorizationUri);
    }

    [Fact]
    public void AuthConfiguration_DefaultsToCommercial()
    {
        var auth = new AuthConfiguration(AuthMode.Certificate, "tenant", "client", "thumb", null, null);

        Assert.Equal(CloudEnvironment.Commercial, auth.Cloud);
    }
}
