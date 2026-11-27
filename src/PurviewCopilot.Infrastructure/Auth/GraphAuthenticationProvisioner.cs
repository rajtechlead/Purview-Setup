using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Auth;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Infrastructure.Auth;

/// <summary>
/// Microsoft Graph implementation of authentication provisioning. Uses the bootstrap credential
/// (an administrator signed in to the app) to create the app registration, service principal,
/// key/secret, owners, least-privilege Graph role assignments and admin consent.
/// </summary>
public sealed class GraphAuthenticationProvisioner : IAuthenticationProvisioningGateway
{
    public const string MicrosoftGraphAppId = GraphPermissionManifest.MicrosoftGraphAppId;

    private readonly EntraTokenProvider _tokens;
    private readonly ConnectionVerifier _verifier;
    private readonly HttpClient _http;

    public GraphAuthenticationProvisioner(EntraTokenProvider tokens, ConnectionVerifier verifier, HttpClient? http = null)
    {
        _tokens = tokens;
        _verifier = verifier;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    }

    public async Task<(string ApplicationId, string ObjectId)> CreateApplicationAsync(
        string displayName, AuthConfiguration bootstrap, CancellationToken cancellationToken = default)
    {
        var graphServicePrincipalId = await ResolveGraphServicePrincipalIdAsync(bootstrap, cancellationToken)
            .ConfigureAwait(false);
        var roles = await ResolveGraphRolesAsync(graphServicePrincipalId, bootstrap, cancellationToken)
            .ConfigureAwait(false);

        var body = new
        {
            displayName,
            signInAudience = "AzureADMyOrg",
            requiredResourceAccess = new[]
            {
                new
                {
                    resourceAppId = MicrosoftGraphAppId,
                    resourceAccess = roles.Select(r => new { id = r.Id, type = "Role" }).ToArray()
                }
            }
        };

        using var document = await SendAsync(HttpMethod.Post, "/applications", bootstrap, body, cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        return (root.GetProperty("appId").GetString()!, root.GetProperty("id").GetString()!);
    }

    public async Task<string> CreateServicePrincipalAsync(
        string applicationId, AuthConfiguration bootstrap, CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Post, "/servicePrincipals", bootstrap,
            new { appId = applicationId }, cancellationToken).ConfigureAwait(false);

        return document.RootElement.GetProperty("id").GetString()!;
    }

    public async Task AddCertificateAsync(
        string objectId, string publicCertificateBase64, AuthConfiguration bootstrap, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            keyCredential = new
            {
                type = "AsymmetricX509Cert",
                usage = "Verify",
                key = publicCertificateBase64
            }
        };

        using var _ = await SendAsync(HttpMethod.Post, $"/applications/{objectId}/addKey", bootstrap, body, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> AddSecretAsync(
        string objectId, string displayName, int months, AuthConfiguration bootstrap, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            passwordCredential = new
            {
                displayName,
                endDateTime = DateTimeOffset.UtcNow.AddMonths(months).ToString("o")
            }
        };

        using var document = await SendAsync(HttpMethod.Post, $"/applications/{objectId}/addPassword", bootstrap, body, cancellationToken)
            .ConfigureAwait(false);

        return document.RootElement.GetProperty("secretText").GetString()!;
    }

    public async Task AddOwnerAsync(
        string objectId, string owner, AuthConfiguration bootstrap, CancellationToken cancellationToken = default)
    {
        var ownerId = Guid.TryParse(owner, out _)
            ? owner
            : await ResolveUserIdAsync(owner, bootstrap, cancellationToken).ConfigureAwait(false);

        var graph = GraphBase(bootstrap);
        using var ownerResponse = await SendAsync(HttpMethod.Post, $"/applications/{objectId}/owners/$ref", bootstrap,
            new Dictionary<string, object> { ["@odata.id"] = $"{graph}/directoryObjects/{ownerId}" },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task GrantGraphAdminConsentAsync(
        string servicePrincipalId, AuthConfiguration bootstrap, CancellationToken cancellationToken = default)
    {
        var graphServicePrincipalId = await ResolveGraphServicePrincipalIdAsync(bootstrap, cancellationToken)
            .ConfigureAwait(false);
        var roles = await ResolveGraphRolesAsync(graphServicePrincipalId, bootstrap, cancellationToken)
            .ConfigureAwait(false);

        // An app-only registration is consented through app role assignments. Delegated scope grants
        // do not apply here, and asking for them would widen the consent beyond what the app uses.
        foreach (var role in roles)
        {
            try
            {
                using var assignment = await SendAsync(HttpMethod.Post,
                    $"/servicePrincipals/{servicePrincipalId}/appRoleAssignments", bootstrap,
                    new { principalId = servicePrincipalId, resourceId = graphServicePrincipalId, appRoleId = role.Id },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                // Consent is idempotent; an existing assignment is the desired state.
            }
        }
    }

    public Task<(bool Ok, string Message)> ValidateAsync(
        AuthConfiguration auth, CancellationToken cancellationToken = default) =>
        _verifier.VerifyAsync(auth, cancellationToken);

    public async Task DeleteApplicationAsync(
        string objectId, string servicePrincipalId, AuthConfiguration bootstrap, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"/servicePrincipals/{servicePrincipalId}", bootstrap, null, cancellationToken)
            .ConfigureAwait(false);
        using var __ = await SendAsync(HttpMethod.Delete, $"/applications/{objectId}", bootstrap, null, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<GraphAppRole>> ResolveGraphRolesAsync(
        string graphServicePrincipalId, AuthConfiguration bootstrap, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"/servicePrincipals/{graphServicePrincipalId}?$select=appRoles", bootstrap, null, cancellationToken)
            .ConfigureAwait(false);

        var (resolved, missing) = GraphAppRoleCatalog.Resolve(document.RootElement, GraphPermissionManifest.Names);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "The Microsoft Graph service principal does not publish these app roles: " +
                $"{string.Join(", ", missing)}. Update the permission manifest before provisioning.");
        }

        return resolved;
    }

    private async Task<string> ResolveUserIdAsync(string userPrincipalName, AuthConfiguration bootstrap, CancellationToken cancellationToken)
    {
        var path = $"/users/{Uri.EscapeDataString(userPrincipalName)}?$select=id";
        using var document = await SendAsync(HttpMethod.Get, path, bootstrap, null, cancellationToken).ConfigureAwait(false);
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<string> ResolveGraphServicePrincipalIdAsync(AuthConfiguration bootstrap, CancellationToken cancellationToken)
    {
        var path = $"/servicePrincipals?$filter=appId eq '{MicrosoftGraphAppId}'&$select=id";
        using var document = await SendAsync(HttpMethod.Get, path, bootstrap, null, cancellationToken).ConfigureAwait(false);

        var value = document.RootElement.GetProperty("value").EnumerateArray().FirstOrDefault();
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("The Microsoft Graph service principal was not found in this tenant.");

        return value.GetProperty("id").GetString()!;
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string path,
        AuthConfiguration bootstrap,
        object? body,
        CancellationToken cancellationToken)
    {
        var token = await _tokens.GetGraphTokenAsync(bootstrap, cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(method, GraphBase(bootstrap) + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var detail = text.Length > 600 ? text[..600] : text;
            throw new InvalidOperationException($"Graph {method} {path} returned {(int)response.StatusCode}: {detail}");
        }

        return string.IsNullOrWhiteSpace(text) ? JsonDocument.Parse("{}") : JsonDocument.Parse(text);
    }

    private static string GraphBase(AuthConfiguration bootstrap) => CloudProfiles.For(bootstrap.Cloud).GraphBaseUrl;
}
