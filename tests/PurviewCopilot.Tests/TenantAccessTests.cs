using PurviewCopilot.Application.Workspace;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class TenantAccessTests
{
    [Fact]
    public void Normalize_AddsOwnerAsAdministrator_AndDeduplicates()
    {
        var access = TenantAccessPolicy.Normalize(new[]
        {
            new TenantAccess("Operator1", UserRole.Operator),
            new TenantAccess("operator1", UserRole.Viewer)
        }, "Owner");

        Assert.Contains(access, a => a.Identity == "Owner" && a.Role == UserRole.Administrator);
        Assert.Single(access, a => a.Identity.Equals("Operator1", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(UserRole.Viewer, access.Single(a => a.Identity.Equals("Operator1", StringComparison.OrdinalIgnoreCase)).Role);
    }

    [Fact]
    public void ParseAssignments_RejectsUnknownRolesAndBadEntries()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TenantAccessPolicy.ParseAssignments("alice=SuperUser", "alice"));
        Assert.Throws<InvalidOperationException>(() =>
            TenantAccessPolicy.ParseAssignments("alice", "alice"));
    }

    [Fact]
    public void ParseAssignments_KeepsExplicitRoles()
    {
        var access = TenantAccessPolicy.ParseAssignments(
            "alice=Operator; bob=Approver; carol=Viewer", "dave");

        Assert.Equal(UserRole.Operator, TenantAccessPolicy.RoleFor(Profile(access), "alice"));
        Assert.Equal(UserRole.Approver, TenantAccessPolicy.RoleFor(Profile(access), "bob"));
        Assert.Equal(UserRole.Viewer, TenantAccessPolicy.RoleFor(Profile(access), "carol"));
        Assert.Null(TenantAccessPolicy.RoleFor(Profile(access), "eve"));
    }

    [Fact]
    public void Roles_AreEnforcedPerAction()
    {
        var profile = Profile(TenantAccessPolicy.ParseAssignments(
            "alice=Operator; bob=Approver; carol=Viewer; dave=Administrator", "dave"));

        Assert.True(TenantAccessPolicy.CanOperate(profile, "alice"));
        Assert.False(TenantAccessPolicy.CanApprove(profile, "alice"));
        Assert.True(TenantAccessPolicy.CanApprove(profile, "bob"));
        Assert.True(TenantAccessPolicy.CanOperate(profile, "bob"));
        Assert.False(TenantAccessPolicy.CanOperate(profile, "carol"));
        Assert.True(TenantAccessPolicy.CanManage(profile, "dave"));
        Assert.False(TenantAccessPolicy.CanManage(profile, "bob"));
        Assert.False(TenantAccessPolicy.CanAccess(profile, "eve"));
    }

    [Fact]
    public void LegacyProfile_IsLimitedToItsRecordedOwner()
    {
        var legacy = new TenantProfile("t", "Legacy", Auth(), DateTimeOffset.UtcNow, "owner");

        Assert.True(TenantAccessPolicy.CanManage(legacy, "owner"));
        Assert.False(TenantAccessPolicy.CanAccess(legacy, "someone-else"));
    }

    private static TenantProfile Profile(IReadOnlyList<TenantAccess> access) =>
        new("t", "Tenant", Auth(), DateTimeOffset.UtcNow, "dave", access);

    private static AuthConfiguration Auth() =>
        new(AuthMode.Certificate, "tenant", "client", "thumb", null, null);
}
