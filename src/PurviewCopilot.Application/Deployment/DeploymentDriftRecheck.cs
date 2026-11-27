using PurviewCopilot.Application.Monitoring;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

public sealed record DriftRecheckOutcome(
    bool Cleared,
    IReadOnlyList<DriftItem> Drift,
    string BaselineFingerprint,
    string CurrentFingerprint)
{
    public string Summary => Cleared
        ? "No tenant drift since the phase was simulated."
        : $"Tenant drift detected: {string.Join("; ", Drift.Select(d => d.Title))}.";
}

/// <summary>
/// The recheck that must happen immediately before execution. It compares the snapshot the phase was
/// simulated and approved against with a fresh read-only collection, so an approval cannot be reused
/// after someone changes the tenant.
/// </summary>
public static class DeploymentDriftRecheck
{
    public static DriftRecheckOutcome Evaluate(TenantSnapshot approved, TenantSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(approved);
        ArgumentNullException.ThrowIfNull(current);

        var drift = DriftDetector.Compare(approved, current);
        var baseline = TenantSnapshotFingerprint.Compute(approved);
        var now = TenantSnapshotFingerprint.Compute(current);

        return new DriftRecheckOutcome(
            drift.Count == 0 && string.Equals(baseline, now, StringComparison.Ordinal),
            drift,
            baseline,
            now);
    }
}
