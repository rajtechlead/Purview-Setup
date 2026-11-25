using Azure.Core;
using Azure.Identity;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Infrastructure.Auth;

/// <summary>
/// Builds the least-privilege credential for the configured auth mode. Certificate auth is the
/// default; client secret is supported only for lab use and is labelled as such in the UI.
/// </summary>
public sealed class EntraTokenProvider
{
    public static readonly string[] GraphScopes = { "https://graph.microsoft.com/.default" };

    private readonly WindowsCertificateService _certificates;

    public EntraTokenProvider(WindowsCertificateService certificates) => _certificates = certificates;

    public TokenCredential CreateCredential(AuthConfiguration auth)
    {
        Validate(auth);

        return auth.Mode switch
        {
            AuthMode.Certificate => new ClientCertificateCredential(
                auth.TenantId,
                auth.ClientId,
                _certificates.LoadForAuthentication(auth.CertificateThumbprint!)),

            AuthMode.ManagedIdentity => string.IsNullOrWhiteSpace(auth.ManagedIdentityClientId)
                ? new ManagedIdentityCredential()
                : new ManagedIdentityCredential(auth.ManagedIdentityClientId),

            AuthMode.Interactive => new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
            {
                TenantId = auth.TenantId,
                ClientId = auth.ClientId,
                RedirectUri = new Uri("http://localhost")
            }),

            AuthMode.ClientSecret => new ClientSecretCredential(
                auth.TenantId, auth.ClientId, auth.ClientSecret),

            _ => throw new NotSupportedException($"Auth mode {auth.Mode} is not supported.")
        };
    }

    public async Task<string> GetGraphTokenAsync(AuthConfiguration auth, CancellationToken cancellationToken = default)
    {
        var credential = CreateCredential(auth);
        var token = await credential.GetTokenAsync(new TokenRequestContext(GraphScopes), cancellationToken)
            .ConfigureAwait(false);
        return token.Token;
    }

    public static void Validate(AuthConfiguration auth)
    {
        if (string.IsNullOrWhiteSpace(auth.TenantId) || !Guid.TryParse(auth.TenantId, out _))
            throw new InvalidOperationException("A valid tenant ID (GUID) is required.");

        if (string.IsNullOrWhiteSpace(auth.ClientId) || !Guid.TryParse(auth.ClientId, out _))
            throw new InvalidOperationException("A valid application (client) ID is required.");

        switch (auth.Mode)
        {
            case AuthMode.Certificate when string.IsNullOrWhiteSpace(auth.CertificateThumbprint):
                throw new InvalidOperationException("Certificate authentication requires a certificate thumbprint.");
            case AuthMode.ClientSecret when string.IsNullOrWhiteSpace(auth.ClientSecret):
                throw new InvalidOperationException("Client secret authentication requires a secret value.");
        }
    }
}
