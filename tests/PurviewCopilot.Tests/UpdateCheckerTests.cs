using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PurviewCopilot.Application.Updates;
using Xunit;

namespace PurviewCopilot.Tests;

public class UpdateCheckerTests
{
    private static readonly byte[] InstallerBytes = Encoding.UTF8.GetBytes("fake-msi-payload");

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static UpdateChecker Checker(string manifestJson, byte[]? download = null)
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("latest.json", StringComparison.OrdinalIgnoreCase)
                ? Json(manifestJson)
                : request.RequestUri.AbsolutePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                    ? Binary(download ?? InstallerBytes)
                    : new HttpResponseMessage(HttpStatusCode.NotFound));

        return new UpdateChecker(new HttpClient(handler));
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Binary(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static string Manifest(string version, string url = "https://updates.example.com/app.msi", string? hash = null) =>
        JsonSerializer.Serialize(new
        {
            version,
            downloadUrl = url,
            sha256 = hash ?? Sha256(InstallerBytes)
        });

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }

    [Fact]
    public async Task NewerVersion_IsReportedAsAvailable()
    {
        var result = await Checker(Manifest("1.2.0")).CheckAsync("https://updates.example.com/latest.json", new Version(1, 0, 0));

        Assert.True(result.UpdateAvailable);
        Assert.Equal("1.0.0", result.CurrentVersion);
        Assert.Equal("1.2.0", result.LatestVersion);
        Assert.NotNull(result.Manifest);
    }

    [Fact]
    public async Task SameOrOlderVersion_IsNotAvailable()
    {
        var result = await Checker(Manifest("1.0.0")).CheckAsync("https://updates.example.com/latest.json", new Version(1, 0, 0));

        Assert.False(result.UpdateAvailable);
        Assert.Null(result.Manifest);
    }

    [Fact]
    public async Task NonHttpsManifestUrl_IsRejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Checker(Manifest("1.2.0")).CheckAsync("http://updates.example.com/latest.json", new Version(1, 0, 0)));
    }

    [Fact]
    public async Task InvalidHash_IsRejectedBeforeDownload()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Checker(Manifest("1.2.0", hash: "not-a-hash")).CheckAsync("https://updates.example.com/latest.json", new Version(1, 0, 0)));
    }

    [Fact]
    public async Task DownloadWithMatchingHash_IsWritten()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pgc-update-" + Guid.NewGuid().ToString("N"));

        try
        {
            var checker = Checker(Manifest("1.2.0"));
            var check = await checker.CheckAsync("https://updates.example.com/latest.json", new Version(1, 0, 0));

            var path = await checker.DownloadAsync(check.Manifest!, directory);

            Assert.True(File.Exists(path));
            Assert.Equal(InstallerBytes, await File.ReadAllBytesAsync(path));
            Assert.EndsWith("PurviewGovernanceCopilot-1.2.0.msi", path, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task DownloadWithWrongHash_ThrowsAndWritesNothing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pgc-update-" + Guid.NewGuid().ToString("N"));

        try
        {
            var manifest = new UpdateManifest("1.2.0", "https://updates.example.com/app.msi", new string('0', 64));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Checker(Manifest("1.2.0")).DownloadAsync(manifest, directory));

            Assert.False(Directory.Exists(directory) && Directory.EnumerateFiles(directory).Any());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
