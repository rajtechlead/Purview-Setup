namespace PurviewCopilot.Application.Auth;

public enum CredentialLifetimeState
{
    Healthy,
    Expiring,
    Expired
}

/// <summary>
/// Client secrets are a lab fallback, never the production default. The policy fixes the default at
/// twelve months, refuses anything past Entra's 24-month maximum and surfaces rotation before expiry.
/// </summary>
public static class ClientSecretPolicy
{
    public const int DefaultLifetimeMonths = 12;
    public const int MaxLifetimeMonths = 24;
    public const int RotationLeadDays = 14;

    public static readonly int[] SupportedMonths = { 3, 6, 12, 24 };

    public static CredentialLifetimeState Evaluate(DateTimeOffset? expiresUtc, DateTimeOffset now)
    {
        if (expiresUtc is null || expiresUtc <= now) return CredentialLifetimeState.Expired;
        return expiresUtc - now <= TimeSpan.FromDays(RotationLeadDays)
            ? CredentialLifetimeState.Expiring
            : CredentialLifetimeState.Healthy;
    }

    public static bool NeedsRotation(DateTimeOffset? expiresUtc, DateTimeOffset now) =>
        Evaluate(expiresUtc, now) != CredentialLifetimeState.Healthy;

    public static void ValidateLifetime(int months)
    {
        if (!SupportedMonths.Contains(months) || months > MaxLifetimeMonths)
            throw new InvalidOperationException(
                $"Secret lifetime must be {string.Join(", ", SupportedMonths[..^1])} or {SupportedMonths[^1]} months, and never more than Entra's {MaxLifetimeMonths}-month maximum.");
    }
}
