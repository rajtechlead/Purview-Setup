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
    public const string MicrosoftGraphAppId = "00000003-0000-0000-c000-000000000000";

    private static readonly (string Name, string Id)[] GraphRoles =
    {
        ("User.Read.All", "df021288-bdef-4463-88db-98f22de89214"),
        ("Directory.Read.All", "7ab1d382-f21e-4acd-a863-ba3e13f7da61"),
        ("Organization.Read.All", "498476ce-e0fe-48b0-b801-37ba7e2685c6"),
        ("SecurityEvents.Read.All", "64733abd-851e-478a-bffb-e47a14b18235"),
        ("AuditLog.Read.All", "b0afded3-3588-46d8-8b3d-9842eff778da")
    };

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
        var body = new
        {
            displayName,
            signInAudience = "AzureADMyOrg",
            requiredResourceAccess = new[]
            {
                new
                {
                    resourceAppId = MicrosoftGraphAppId,
                    resourceAccess = GraphRoles.Select(r => new { id = r.Id, type = "Role" }).ToArray()
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

        foreach (var (_, roleId) in GraphRoles)
        {
            try
            {
                using var assignment = await SendAsync(HttpMethod.Post,
                    $"/servicePrincipals/{servicePrincipalId}/appRoleAssignments", bootstrap,
                    new { principalId = servicePrincipalId, resourceId = graphServicePrincipalId, appRoleId = roleId },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                // Consent is idempotent; an existing assignment is the desired state.
            }
        }

        try
        {
            using var grant = await SendAsync(HttpMethod.Post, "/oauth2PermissionGrants", bootstrap, new
            {
                clientId = servicePrincipalId,
                consentType = "AllPrincipals",
                resourceId = graphServicePrincipalId,
                scope = string.Join(' ', GraphRoles.Select(r => r.Name))
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
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
