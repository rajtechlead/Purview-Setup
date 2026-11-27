using System.Security.Cryptography;
using System.Text.Json;
using PurviewCopilot.Application.Release;
using Xunit;

namespace PurviewCopilot.Tests;

public class ReleaseManifestTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"pgc-release-{Guid.NewGuid():N}");

    public ReleaseManifestTests() => Directory.CreateDirectory(_directory);

    private string File(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ManifestHashesEveryArtefactAndTheSbom()
    {
        var msi = File("app.msi", "installer-bytes");
        var sbom = File("bom.json", "{\"bomFormat\":\"CycloneDX\"}");

        var manifest = ReleaseManifest.Create("1.0.0", "abc1234", new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero), new[] { msi }, sbom);

        var expectedMsi = Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(msi))).ToLowerInvariant();
        var expectedSbom = Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(sbom))).ToLowerInvariant();

        Assert.Equal(expectedMsi, manifest.Files.Single().Sha256);
        Assert.Equal("app.msi", manifest.Files.Single().Name);
        Assert.Equal(expectedSbom, manifest.SbomSha256);
        Assert.Equal("bom.json", manifest.Sbom);
    }

    [Fact]
    public void JsonIsCamelCaseAndCarriesIdentity()
    {
        var manifest = ReleaseManifest.Create(
            "1.2.3", "deadbee", new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero),
            new[] { File("setup.msi", "x") });

        using var document = JsonDocument.Parse(manifest.ToJson());
        var root = document.RootElement;

        Assert.Equal("Purview Governance Copilot", root.GetProperty("product").GetString());
        Assert.Equal("1.2.3", root.GetProperty("version").GetString());
        Assert.Equal("deadbee", root.GetProperty("commit").GetString());
        Assert.Equal(1, root.GetProperty("files").GetArrayLength());
        Assert.False(root.TryGetProperty("sbom", out _));
    }

    [Fact]
    public void MissingArtefactFailsInsteadOfProducingPartialProvenance()
    {
        Assert.Throws<FileNotFoundException>(() => ReleaseManifest.Create(
            "1.0.0", "abc", DateTimeOffset.UtcNow, new[] { Path.Combine(_directory, "absent.msi") }));
    }

    [Fact]
    public void ManifestRequiresAtLeastOneArtefact()
    {
        Assert.Throws<InvalidOperationException>(() => ReleaseManifest.Create(
            "1.0.0", "abc", DateTimeOffset.UtcNow, Array.Empty<string>()));
    }

    [Fact]
    public void HashingIsStableForIdenticalContent()
    {
        var first = File("a.msi", "same");
        var second = File("b.msi", "same");

        Assert.Equal(ReleaseManifest.HashFile(first).Sha256, ReleaseManifest.HashFile(second).Sha256);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
