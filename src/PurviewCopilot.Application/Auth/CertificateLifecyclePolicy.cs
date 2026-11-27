using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Auth;

public enum CertificateLifetimeState
{
    Healthy,
    Expiring,
    Expired
}

public sealed record RotationPlan(
    bool Required,
    DateTimeOffset RetireOldAfterUtc,
    IReadOnlyList<string> Steps);

/// <summary>
/// Certificate-first authentication policy: a 12-month default, a warning before expiry, and an
/// overlap rotation where the replacement is added and validated before the old key is removed.
/// </summary>
public static class CertificateLifecyclePolicy
{
    public const int DefaultValidityMonths = 12;
    public const int RotationLeadDays = 30;
    public const int MinimumOverlapDays = 7;
    public const int MaxUnacknowledgedMonths = 24;

    public static readonly int[] SupportedValidityMonths = { 6, 12, 24, 36, 60, 120 };

    public static CertificateLifetimeState Evaluate(CertificateInfo certificate, DateTimeOffset now)
    {
        if (certificate.NotAfter <= now) return CertificateLifetimeState.Expired;
        return certificate.NotAfter - now <= TimeSpan.FromDays(RotationLeadDays)
            ? CertificateLifetimeState.Expiring
            : CertificateLifetimeState.Healthy;
    }

    public static RotationPlan PlanRotation(string subject, CertificateInfo current, DateTimeOffset now)
    {
        var state = Evaluate(current, now);
        var retireAfter = now.AddDays(MinimumOverlapDays);

        return new RotationPlan(
            state != CertificateLifetimeState.Healthy,
            retireAfter,
            new[]
            {
                $"Create a replacement certificate for '{subject}' valid {DefaultValidityMonths} months and install it in CurrentUser\\My.",
                "Upload its public key to the application as an additional keyCredential; keep the current credential.",
                "Validate authentication with the replacement thumbprint before changing configuration.",
                $"Switch the stored thumbprint to the replacement and keep the old credential for at least {MinimumOverlapDays} days of overlap.",
                $"Retire the old credential after {retireAfter:u}: remove the old keyCredential, then delete the certificate from the store."
            });
    }

    public static void ValidateLifetime(int months, bool acknowledgeLongLived)
    {
        if (!SupportedValidityMonths.Contains(months))
            throw new InvalidOperationException(
                $"Certificate lifetime must be {string.Join(", ", SupportedValidityMonths[..^1])} or {SupportedValidityMonths[^1]} months.");

        if (months > MaxUnacknowledgedMonths && !acknowledgeLongLived)
            throw new InvalidOperationException(
                $"A certificate longer than {MaxUnacknowledgedMonths} months requires explicit acknowledgement.");
    }

}
