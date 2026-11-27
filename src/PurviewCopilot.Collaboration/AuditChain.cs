using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PurviewCopilot.Collaboration;

/// <summary>
/// Canonical hash for one shared audit entry. The chain is per tenant and independent of insertion
/// identifiers, so an edited, deleted or reordered row is detected at the first affected sequence.
/// </summary>
public static class AuditChain
{
    public const string Genesis = "";

    public static string Compute(
        string previousHash,
        string tenantId,
        int sequence,
        DateTimeOffset atUtc,
        string actor,
        string action,
        string subject,
        string detail)
    {
        var canonical = string.Join(
            '\u001f',
            previousHash ?? string.Empty,
            tenantId ?? string.Empty,
            sequence.ToString(CultureInfo.InvariantCulture),
            atUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            actor ?? string.Empty,
            action ?? string.Empty,
            subject ?? string.Empty,
            detail ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
