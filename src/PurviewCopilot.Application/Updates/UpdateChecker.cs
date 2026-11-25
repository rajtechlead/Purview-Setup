using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace PurviewCopilot.Application.Updates;

/// <summary>Published update descriptor. Only https URLs and a 64-character SHA-256 are accepted.</summary>
public sealed record UpdateManifest(string Version, string DownloadUrl, string Sha256, string? Notes = null);

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string CurrentVersion,
    string? LatestVersion,
    string Message,
    UpdateManifest? Manifest = null);

/// <summary>
/// Checks a signed-in-CI update manifest and downloads the installer only after its SHA-256
/// matches. Installing remains an explicit operator action; the updater never launches the MSI.
/// </summary>
public sealed class UpdateChecker
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public UpdateChecker(HttpClient http) => _http = http;

    public async Task<UpdateCheckResult> CheckAsync(
        string manifestUrl,
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        var uri = RequireHttps(manifestUrl, "manifest");
        var json = await _http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, Json)
            ?? throw new InvalidOperationException("Update manifest is empty.");

        if (!Version.TryParse(manifest.Version, out var latest))
        {
            throw new InvalidOperationException($"Update manifest version '{manifest.Version}' is not valid.");
        }

        RequireHttps(manifest.DownloadUrl, "download");

        if (!IsSha256(manifest.Sha256))
        {
            throw new InvalidOperationException("Update manifest sha256 must be 64 hexadecimal characters.");
        }

        var current = Format(currentVersion);
        var latestText = Format(latest);

        return latest > currentVersion
            ? new UpdateCheckResult(true, current, latestText, $"Version {latestText} is available.", manifest with { Version = latestText })
            : new UpdateCheckResult(false, current, latestText, $"Version {current} is current.");
    }

    public async Task<string> DownloadAsync(
        UpdateManifest manifest,
        string directory,
        CancellationToken cancellationToken = default)
    {
        RequireHttps(manifest.DownloadUrl, "download");
        Directory.CreateDirectory(directory);

        var bytes = await _http.GetByteArrayAsync(manifest.DownloadUrl, cancellationToken).ConfigureAwait(false);
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        if (!string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Downloaded update failed SHA-256 verification; the file was discarded.");
        }

        var path = Path.Combine(directory, $"PurviewGovernanceCopilot-{manifest.Version}.msi");
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static Uri RequireHttps(string? value, string label) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : throw new InvalidOperationException($"The update {label} URL must be an absolute https URL.");

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string Format(Version version) =>
        version.Revision > 0 ? version.ToString(4) : version.ToString(3);
}
