using System.Security.Cryptography;
using PurviewCopilot.Application.Modules;
using Xunit;

namespace PurviewCopilot.Tests;

public class PluginTrustTests
{
    private static byte[] Assembly() => "pretend-assembly-bytes"u8.ToArray();

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static PluginManifest Manifest(byte[] assembly, RSA key, params string[] permissions) =>
        new(
            "contoso.module",
            "1.2.0",
            "Contoso",
            permissions.Length == 0 ? new[] { PluginPermissions.AssessmentRead } : permissions,
            Hash(assembly),
            key.ExportSubjectPublicKeyInfoPem(),
            string.Empty);

    [Fact]
    public void SignedManifestWithAllowedPermissionIsAccepted()
    {
        using var key = RSA.Create(2048);
        var assembly = Assembly();
        var signed = PluginTrustPolicy.Sign(Manifest(assembly, key), key);

        var result = PluginTrustPolicy.Validate(signed, "contoso.module.dll", assembly);

        Assert.True(result.Trusted, result.Reason);
        Assert.Contains("Signed manifest", result.Reason);
    }

    [Fact]
    public void UnsignedManifestIsRejectedByDefault()
    {
        using var key = RSA.Create(2048);
        var assembly = Assembly();

        var result = PluginTrustPolicy.Validate(Manifest(assembly, key), "contoso.module.dll", assembly);

        Assert.False(result.Trusted);
        Assert.Contains("not signed", result.Reason);
    }

    [Fact]
    public void TamperedAssemblyIsRejected()
    {
        using var key = RSA.Create(2048);
        var assembly = Assembly();
        var signed = PluginTrustPolicy.Sign(Manifest(assembly, key), key);

        var result = PluginTrustPolicy.Validate(signed, "contoso.module.dll", "modified-bytes"u8.ToArray());

        Assert.False(result.Trusted);
        Assert.Contains("hash does not match", result.Reason);
    }

    [Fact]
    public void PermissionsOutsideTheAllowlistAreRejected()
    {
        using var key = RSA.Create(2048);
        var assembly = Assembly();
        var signed = PluginTrustPolicy.Sign(Manifest(assembly, key, "assessment:read", "network:any"), key);

        var result = PluginTrustPolicy.Validate(signed, "contoso.module.dll", assembly);

        Assert.False(result.Trusted);
        Assert.Contains("network:any", result.Reason);
    }

    [Fact]
    public void ManifestMustMatchTheFileAndDeclareAVersion()
    {
        using var key = RSA.Create(2048);
        var assembly = Assembly();
        var signed = PluginTrustPolicy.Sign(Manifest(assembly, key), key);

        Assert.False(PluginTrustPolicy.Validate(signed, "other.module.dll", assembly).Trusted);
        Assert.False(PluginTrustPolicy.Validate(signed with { Version = "" }, "contoso.module.dll", assembly).Trusted);
    }

    [Fact]
    public void MalformedOrMissingManifestIsRejected()
    {
        var assembly = Assembly();

        Assert.Null(PluginTrustPolicy.ParseManifest("{ not json"));
        Assert.False(PluginTrustPolicy.Validate(null, "contoso.module.dll", assembly).Trusted);
    }

    [Fact]
    public void SignatureFromAnotherKeyIsRejected()
    {
        using var signer = RSA.Create(2048);
        using var other = RSA.Create(2048);
        var assembly = Assembly();

        // Hash matches, key in the manifest is `other`, signature is by `signer`.
        var manifest = Manifest(assembly, other) with
        {
            Signature = Convert.ToBase64String(signer.SignHash(
                Convert.FromHexString(Hash(assembly)), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        };

        var result = PluginTrustPolicy.Validate(manifest, "contoso.module.dll", assembly);

        Assert.False(result.Trusted);
        Assert.Contains("signature is not valid", result.Reason);
    }

    [Fact]
    public void LoaderSkipsUnsignedOrUnmanifestedAssemblies()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pgc-plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllBytes(Path.Combine(directory, "unsigned.dll"), Assembly());
            File.WriteAllText(Path.Combine(directory, "unsigned.plugin.json"), "{}");

            var result = PluginModuleLoader.Load(directory);

            Assert.Empty(result.Modules);
            Assert.Contains(result.Messages, m => m.Contains("unsigned.dll", StringComparison.Ordinal) &&
                                                  m.Contains("rejected", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
