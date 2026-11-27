using System.Text.Json;
using PurviewCopilot.Application.Auth;
using PurviewCopilot.Infrastructure.Auth;
using Xunit;

namespace PurviewCopilot.Tests;

public class GraphPermissionManifestTests
{
    [Fact]
    public void ManifestCoversEveryCollectorApi()
    {
        // One permission per Graph API family the read-only collector calls.
        string[] needed =
        {
            "User.Read.All", "Group.Read.All", "Application.Read.All", "Organization.Read.All",
            "Domain.Read.All", "Policy.Read.All", "RoleManagement.Read.Directory",
            "SecurityEvents.Read.All", "AuditLog.Read.All", "Reports.Read.All",
            "DeviceManagementManagedDevices.Read.All", "Sites.Read.All", "Team.ReadBasic.All"
        };

        Assert.All(needed, name => Assert.Contains(name, GraphPermissionManifest.Names, StringComparer.Ordinal));
    }

    [Fact]
    public void ManifestIsReadOnlyAndUsesNoBroadDirectoryScope()
    {
        Assert.True(GraphPermissionManifest.IsReadOnly(GraphPermissionManifest.Names));
        Assert.DoesNotContain("Directory.Read.All", GraphPermissionManifest.Names);
        Assert.DoesNotContain(GraphPermissionManifest.Names, n =>
            n.Contains("ReadWrite", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("FullControl", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryEntryDocumentsItsFeatureAndReason()
    {
        Assert.All(GraphPermissionManifest.Required, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Feature));
            Assert.False(string.IsNullOrWhiteSpace(p.Reason));
        });
    }

    [Fact]
    public void CatalogResolvesNamesToPublishedRoleIds()
    {
        using var document = JsonDocument.Parse("""
            {
              "appRoles": [
                { "id": "11111111-1111-1111-1111-111111111111", "value": "User.Read.All", "isEnabled": true },
                { "id": "22222222-2222-2222-2222-222222222222", "value": "Policy.Read.All", "isEnabled": true }
              ]
            }
            """);

        var (resolved, missing) = GraphAppRoleCatalog.Resolve(document.RootElement, new[] { "user.read.all", "Policy.Read.All" });

        Assert.Empty(missing);
        Assert.Equal(2, resolved.Count);
        Assert.Equal("11111111-1111-1111-1111-111111111111", resolved.Single(r => r.Name == "User.Read.All").Id);
    }

    [Fact]
    public void CatalogReportsMissingPermissionsInsteadOfInventingIds()
    {
        using var document = JsonDocument.Parse("""{ "appRoles": [ { "id": "x", "value": "User.Read.All" } ] }""");

        var (resolved, missing) = GraphAppRoleCatalog.Resolve(document.RootElement, new[] { "User.Read.All", "Directory.ReadWrite.All" });

        Assert.Single(resolved);
        Assert.Equal(new[] { "Directory.ReadWrite.All" }, missing);
    }

    [Fact]
    public void CatalogToleratesMalformedRoles()
    {
        using var document = JsonDocument.Parse("""
            { "appRoles": [ null, { "value": "User.Read.All" }, { "id": "abc" }, { "id": "id", "value": "Team.ReadBasic.All" } ] }
            """);

        var (resolved, missing) = GraphAppRoleCatalog.Resolve(document.RootElement, new[] { "User.Read.All", "Team.ReadBasic.All" });

        Assert.Single(resolved);
        Assert.Equal("Team.ReadBasic.All", resolved[0].Name);
        Assert.Single(missing);
    }

    [Fact]
    public void CatalogHandlesAServicePrincipalWithoutRoles()
    {
        using var document = JsonDocument.Parse("""{ "id": "sp" }""");

        var (resolved, missing) = GraphAppRoleCatalog.Resolve(document.RootElement, new[] { "User.Read.All" });

        Assert.Empty(resolved);
        Assert.Single(missing);
    }
}
