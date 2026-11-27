using System.Security.Cryptography;
using System.Text.Json;

namespace PurviewCopilot.Application.Modules;

/// <summary>
/// What a plugin is allowed to declare. Assessment modules only read the collected snapshot, so
/// anything that implies network, file or tenant-write access is refused rather than negotiated.
/// </summary>
public static class PluginPermissions
{
    public const string AssessmentRead = "assessment:read";

    public static IReadOnlySet<string> Allowed { get; } =
        new HashSet<string>(StringComparer.Ordinal) { AssessmentRead };
}

public sealed record PluginManifest(
    string Name,
    string Version,
    string Publisher,
    IReadOnlyList<string> Permissions,
    string Sha256,
    string PublicKeyPem,
    string Signature);

public sealed record PluginTrustResult(bool Trusted, string Reason)
{
    public static PluginTrustResult Rejected(string reason) => new(false, reason);
    public static PluginTrustResult Accepted() => new(true, "Signed manifest verified.");
}

/// <summary>
/// Verifies a plugin before its assembly is loaded: the manifest must describe the same file, declare
/// only allowed permissions, match the DLL hash and carry a valid signature from the declared key.
/// Unsigned plugins are refused; loading third-party code is not a default.
/// </summary>
public static class PluginTrustPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static PluginManifest? ParseManifest(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PluginManifest>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static PluginTrustResult Validate(
        PluginManifest? manifest,
        string assemblyFileName,
        ReadOnlySpan<byte> assemblyBytes,
        bool requireSignature = true)
    {
        if (manifest is null)
        {
            return PluginTrustResult.Rejected("The plugin manifest is missing or not valid JSON.");
        }

        var expectedName = Path.GetFileNameWithoutExtension(assemblyFileName);
        if (string.IsNullOrWhiteSpace(manifest.Name) ||
            !string.Equals(manifest.Name, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            return PluginTrustResult.Rejected(
                $"The manifest name '{manifest.Name}' does not match '{expectedName}'.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            return PluginTrustResult.Rejected("The manifest must declare a version.");
        }

        var unknown = manifest.Permissions
            .Where(p => !PluginPermissions.Allowed.Contains(p))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (unknown.Length > 0)
        {
            return PluginTrustResult.Rejected(
                $"The plugin requests permissions it cannot have: {string.Join(", ", unknown)}.");
        }

        var actualHash = Convert.ToHexString(SHA256.HashData(assemblyBytes)).ToLowerInvariant();
        if (!string.Equals(manifest.Sha256, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            return PluginTrustResult.Rejected("The assembly hash does not match the manifest; the file changed after signing.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Signature) || string.IsNullOrWhiteSpace(manifest.PublicKeyPem))
        {
            return requireSignature
                ? PluginTrustResult.Rejected("The plugin is not signed.")
                : PluginTrustResult.Accepted();
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(manifest.PublicKeyPem);

            var hash = Convert.FromHexString(actualHash);
            var signature = Convert.FromBase64String(manifest.Signature);

            return rsa.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                ? PluginTrustResult.Accepted()
                : PluginTrustResult.Rejected("The signature is not valid for this assembly.");
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return PluginTrustResult.Rejected($"The signature could not be verified: {ex.Message}");
        }
    }

    /// <summary>Signs a manifest for a publisher build step; the private key never ships.</summary>
    public static PluginManifest Sign(PluginManifest unsigned, RSA privateKey)
    {
        var signature = privateKey.SignHash(
            Convert.FromHexString(unsigned.Sha256), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return unsigned with { Signature = Convert.ToBase64String(signature) };
    }
}
