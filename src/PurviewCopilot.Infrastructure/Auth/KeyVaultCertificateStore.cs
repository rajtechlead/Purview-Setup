using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using PurviewCopilot.Application.Abstractions;

namespace PurviewCopilot.Infrastructure.Auth;

/// <summary>
/// Stores the app certificate PFX as a Key Vault secret (base64) using the vault REST API and a
/// managed identity / developer credential. Kept as an optional provider: the Windows store is
/// always available, Key Vault is for teams that need portability and central rotation.
/// </summary>
public sealed class KeyVaultCertificateStore : IKeyVaultCertificateStore
{
    private static readonly string[] Scopes = { "https://vault.azure.net/.default" };
    private readonly HttpClient _http;
    private readonly TokenCredential _credential;
    private readonly string _vaultUri;

    public KeyVaultCertificateStore(string vaultUri, HttpClient? http = null, TokenCredential? credential = null)
    {
        if (!Uri.TryCreate(vaultUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Key Vault URI must be an absolute https URI.", nameof(vaultUri));

        _vaultUri = vaultUri.TrimEnd('/');
        _http = http ?? new HttpClient();
        _credential = credential ?? new DefaultAzureCredential();
    }

    public async Task<string?> DownloadPfxBase64Async(string secretName, CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, secretName, cancellationToken).ConfigureAwait(false);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return document.RootElement.TryGetProperty("value", out var value) ? value.GetString() : null;
    }

    public async Task UploadPfxBase64Async(string secretName, string pfxBase64, CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Put, secretName, cancellationToken).ConfigureAwait(false);
        request.Content = JsonContent.Create(new { value = pfxBase64 });
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string secretName, CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken).ConfigureAwait(false);
        var request = new HttpRequestMessage(method, $"{_vaultUri}/secrets/{Uri.EscapeDataString(secretName)}?api-version=7.4");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return request;
    }
}
