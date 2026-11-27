using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

/// <summary>
/// Fingerprints exactly the tenant signals the drift rules watch. A change here means the state an
/// approval was based on no longer holds, so the approval must not be reused for execution.
/// </summary>
public static class TenantSnapshotFingerprint
{
    public static string Compute(TenantSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var canonical = string.Join(
            '\u001f',
            snapshot.TenantId ?? string.Empty,
            snapshot.UnifiedAuditEnabled,
            Int(snapshot.AuditRetentionDays),
            Int(snapshot.SensitivityLabelCount),
            Int(snapshot.DlpEnforcedCount),
            snapshot.EndpointDlpEnabled,
            Int(snapshot.DevicesOnboarded),
            Int(snapshot.GlobalAdministrators),
            Int(snapshot.ConditionalAccessPolicies),
            Int(snapshot.AppRegistrationsWithSecrets),
            Int(snapshot.RetentionLabelCount),
            snapshot.SecureScorePercent.ToString("0.###", CultureInfo.InvariantCulture));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}
