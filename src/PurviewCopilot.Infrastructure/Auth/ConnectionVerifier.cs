using System.Net.Http.Headers;
using System.Text.Json;
using PurviewCopilot.Application.Auth;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Infrastructure.Auth;

/// <summary>Validates that the configured credential can obtain a Graph token and read the organization object.</summary>
public sealed class ConnectionVerifier
{
    private readonly EntraTokenProvider _tokens;
    private readonly HttpClient _http;

    public ConnectionVerifier(EntraTokenProvider tokens, HttpClient? http = null)
    {
        _tokens = tokens;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<(bool Ok, string Message)> VerifyAsync(AuthConfiguration auth, CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await _tokens.GetGraphTokenAsync(auth, cancellationToken).ConfigureAwait(false);
            var graph = CloudProfiles.For(auth.Cloud).GraphBaseUrl;

            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{graph}/organization?$select=id,displayName");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Token acquired, but Graph returned {(int)response.StatusCode}. Confirm Organization.Read.All consent.");
            }

            using var document = JsonDocument.Parse(body);
            var value = document.RootElement.GetProperty("value").EnumerateArray().FirstOrDefault();
            var name = value.ValueKind == JsonValueKind.Object ? value.GetProperty("displayName").GetString() : null;

            return (true, $"Connected to {name ?? "tenant"}. Token and Graph permissions validated.");
        }
        catch (Exception ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
    }
}
