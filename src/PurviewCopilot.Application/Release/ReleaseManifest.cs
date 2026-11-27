using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PurviewCopilot.Application.Release;

public sealed record ReleaseFile(string Name, long Bytes, string Sha256);

public sealed record ReleaseManifest(
    string Product,
    string Version,
    string Commit,
    DateTimeOffset GeneratedUtc,
    string? Sbom,
    string? SbomSha256,
    IReadOnlyList<ReleaseFile> Files)
{
    public const string ProductName = "Purview Governance Copilot";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Builds provenance for a release: every artefact's SHA-256, the commit it was built from and
    /// the SBOM digest. Missing files fail the build rather than producing incomplete provenance.
    /// </summary>
    public static ReleaseManifest Create(
        string version,
        string commit,
        DateTimeOffset generatedUtc,
        IEnumerable<string> artefactPaths,
        string? sbomPath = null)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("A release version is required.", nameof(version));

        var files = artefactPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(HashFile)
            .ToArray();

        if (files.Length == 0)
            throw new InvalidOperationException("At least one release artefact is required for provenance.");

        string? sbomName = null;
        string? sbomHash = null;

        if (!string.IsNullOrWhiteSpace(sbomPath))
        {
            var sbom = HashFile(sbomPath);
            sbomName = sbom.Name;
            sbomHash = sbom.Sha256;
        }

        return new ReleaseManifest(ProductName, version.Trim(), commit.Trim(), generatedUtc, sbomName, sbomHash, files);
    }

    public static ReleaseFile HashFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Release artefact not found: {path}", path);

        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return new ReleaseFile(Path.GetFileName(path), stream.Length, hash);
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
