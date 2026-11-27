using PurviewCopilot.Application.Deployment;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class DeploymentDriftRecheckTests
{
    private static TenantSnapshot Baseline() => new()
    {
        TenantId = "contoso.onmicrosoft.com",
        UnifiedAuditEnabled = true,
        AuditRetentionDays = 365,
        SensitivityLabelCount = 5,
        DlpEnforcedCount = 2,
        EndpointDlpEnabled = true,
        DevicesOnboarded = 50,
        GlobalAdministrators = 3,
        ConditionalAccessPolicies = 4,
        AppRegistrationsWithSecrets = 1,
        RetentionLabelCount = 6,
        SecureScorePercent = 72
    };

    [Fact]
    public void UnchangedTenant_ClearsTheRecheck()
    {
        var outcome = DeploymentDriftRecheck.Evaluate(Baseline(), Baseline());

        Assert.True(outcome.Cleared);
        Assert.Empty(outcome.Drift);
        Assert.Equal(outcome.BaselineFingerprint, outcome.CurrentFingerprint);
        Assert.Contains("No tenant drift", outcome.Summary);
    }

    [Fact]
    public void DisabledAudit_BlocksAndNamesTheDrift()
    {
        var current = Baseline();
        current.UnifiedAuditEnabled = false;

        var outcome = DeploymentDriftRecheck.Evaluate(Baseline(), current);

        Assert.False(outcome.Cleared);
        Assert.Contains(outcome.Drift, d => d.Id == "DRF-001");
        Assert.Contains("Unified audit log was disabled", outcome.Summary);
    }

    [Fact]
    public void FingerprintChangesForEveryDriftSignal()
    {
        var baseline = Baseline();
        var original = TenantSnapshotFingerprint.Compute(baseline);

        var mutations = new Action<TenantSnapshot>[]
        {
            s => s.UnifiedAuditEnabled = false,
            s => s.AuditRetentionDays = 180,
            s => s.SensitivityLabelCount = 4,
            s => s.DlpEnforcedCount = 1,
            s => s.EndpointDlpEnabled = false,
            s => s.DevicesOnboarded = 10,
            s => s.GlobalAdministrators = 6,
            s => s.ConditionalAccessPolicies = 1,
            s => s.AppRegistrationsWithSecrets = 5,
            s => s.RetentionLabelCount = 1,
            s => s.SecureScorePercent = 40
        };

        foreach (var mutate in mutations)
        {
            var copy = Baseline();
            mutate(copy);
            Assert.NotEqual(original, TenantSnapshotFingerprint.Compute(copy));
        }
    }

    [Fact]
    public void FingerprintIsStableAcrossInstances()
    {
        Assert.Equal(TenantSnapshotFingerprint.Compute(Baseline()), TenantSnapshotFingerprint.Compute(Baseline()));
    }

    [Fact]
    public void ImprovedTenant_StillInvalidatesTheApproval()
    {
        // DriftDetector only reports regressions, so the fingerprint must catch improvements too:
        // an approval for the old state must not silently apply to a different one.
        var current = Baseline();
        current.SensitivityLabelCount = 9;

        var outcome = DeploymentDriftRecheck.Evaluate(Baseline(), current);

        Assert.False(outcome.Cleared);
        Assert.Empty(outcome.Drift);
        Assert.NotEqual(outcome.BaselineFingerprint, outcome.CurrentFingerprint);
    }
}
