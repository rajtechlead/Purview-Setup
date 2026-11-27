using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Auth;

/// <summary>
/// Guided, one-click and MSP onboarding: creates the Entra application, service principal,
/// certificate or lab secret, owners and Graph consent, then validates the resulting credential.
/// Dry run performs every check and returns the plan and rollback script without changing the tenant.
/// </summary>
public sealed class AuthenticationProvisioningService
{
    public static readonly int[] SupportedValidityMonths = CertificateLifecyclePolicy.SupportedValidityMonths;
    public static readonly int[] SupportedSecretMonths = { 3, 6, 12, 24 };

    private readonly IAuthenticationProvisioningGateway _gateway;
    private readonly ICertificateService _certificates;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public AuthenticationProvisioningService(
        IAuthenticationProvisioningGateway gateway,
        ICertificateService certificates,
        IAuditLog audit,
        IClock clock)
    {
        _gateway = gateway;
        _certificates = certificates;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ProvisioningResult> ProvisionAsync(
        ProvisioningRequest request,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<ProvisioningStep>();
        string? createdApplicationId = null;

        try
        {
            Validate(request);

            if (request.DryRun)
            {
                steps.Add(new ProvisioningStep("Validate request", "planned", "Separation of duties, validity and environment checks passed."));
                steps.Add(new ProvisioningStep("Create application", "planned", $"Display name '{request.DisplayName}', single tenant."));
                steps.Add(new ProvisioningStep("Create service principal", "planned", "Enterprise application and service principal."));
                steps.Add(request.Mode == AuthMode.ClientSecret
                    ? new ProvisioningStep("Create client secret", "planned", $"{request.SecretMonths}-month secret; lab only; DPAPI at rest.")
                    : new ProvisioningStep("Create certificate", "planned", $"{request.Algorithm}, {request.CertificateMonths} months, CurrentUser\\My, uploaded as keyCredentials."));
                steps.Add(new ProvisioningStep("Assign owners", "planned", request.Owners.Count == 0 ? "No additional owners." : string.Join(", ", request.Owners)));
                steps.Add(new ProvisioningStep("Grant Graph permissions", "planned",
                    $"{string.Join(", ", GraphPermissionManifest.Names)} with admin consent (app roles resolved against the tenant's Graph service principal)."));
                steps.Add(new ProvisioningStep("Validate authentication", "planned", "Acquire a token and read the organization object."));

                await SafeAuditAsync(request.Operator, "AuthProvisioningDryRun", request.TenantId,
                    $"Mode={request.Mode}; Algorithm={request.Algorithm}; Months={request.CertificateMonths}", cancellationToken)
                    .ConfigureAwait(false);

                return new ProvisioningResult(
                    true,
                    "Dry run complete. No tenant change was made.",
                    null, null, null, null, null,
                    BuildRollbackScript(request, "<application-object-id>", "<service-principal-id>", "<thumbprint>"),
                    steps);
            }

            var application = await _gateway.CreateApplicationAsync(request.DisplayName, request.BootstrapAuth, cancellationToken)
                .ConfigureAwait(false);
            createdApplicationId = application.ApplicationId;
            steps.Add(new ProvisioningStep("Create application", "done", $"Application {application.ApplicationId}."));

            var servicePrincipalId = await _gateway.CreateServicePrincipalAsync(application.ApplicationId, request.BootstrapAuth, cancellationToken)
                .ConfigureAwait(false);
            steps.Add(new ProvisioningStep("Create service principal", "done", $"Service principal {servicePrincipalId}."));

            string? thumbprint = null;
            string? secret = null;

            if (request.Mode == AuthMode.ClientSecret)
            {
                secret = await _gateway.AddSecretAsync(application.ObjectId, request.DisplayName, request.SecretMonths,
                    request.BootstrapAuth, cancellationToken).ConfigureAwait(false);
                steps.Add(new ProvisioningStep("Create client secret", "done", $"{request.SecretMonths}-month secret created; it is stored through the protected settings store."));
            }
            else
            {
                var certificate = _certificates.CreateSelfSigned(
                    $"CN={request.DisplayName}", request.CertificateMonths, installToStore: true, request.Algorithm);

                thumbprint = certificate.Thumbprint;
                var publicKey = _certificates.ExportPublicDerBase64(certificate.Thumbprint);

                await _gateway.AddCertificateAsync(application.ObjectId, publicKey, request.BootstrapAuth, cancellationToken)
                    .ConfigureAwait(false);
                steps.Add(new ProvisioningStep("Create certificate", "done",
                    $"{request.Algorithm} certificate {certificate.Thumbprint} installed in CurrentUser\\My and uploaded as keyCredentials."));
            }

            foreach (var owner in request.Owners.Where(o => !string.IsNullOrWhiteSpace(o)))
            {
                await _gateway.AddOwnerAsync(application.ObjectId, owner.Trim(), request.BootstrapAuth, cancellationToken)
                    .ConfigureAwait(false);
                steps.Add(new ProvisioningStep("Assign owner", "done", owner.Trim()));
            }

            await _gateway.GrantGraphAdminConsentAsync(servicePrincipalId, request.BootstrapAuth, cancellationToken)
                .ConfigureAwait(false);
            steps.Add(new ProvisioningStep("Grant Graph permissions", "done", "Least-privilege Graph roles assigned and admin consent granted."));
            steps.Add(new ProvisioningStep("Admin consent hand-off", "done",
                $"Consent was granted programmatically. If a re-grant is ever needed, an administrator can open {ConsentLink(request, application.ApplicationId)}."));

            var auth = new AuthConfiguration(
                request.Mode,
                request.TenantId,
                application.ApplicationId,
                thumbprint,
                secret,
                null,
                request.BootstrapAuth.Cloud);

            var (ok, message) = await _gateway.ValidateAsync(auth, cancellationToken).ConfigureAwait(false);
            steps.Add(new ProvisioningStep("Validate authentication", ok ? "done" : "failed", message));

            await SafeAuditAsync(request.Operator, "AuthProvisioned", request.TenantId,
                $"ApplicationId={application.ApplicationId}; ServicePrincipal={servicePrincipalId}; Mode={request.Mode}; Approver={request.Approver}; Role={request.ApproverRole}",
                cancellationToken).ConfigureAwait(false);

            return new ProvisioningResult(
                ok,
                ok ? "Authentication provisioned and validated." : $"Provisioned, but validation failed: {message}",
                application.ApplicationId,
                application.ObjectId,
                servicePrincipalId,
                thumbprint,
                ok ? auth : auth,
                BuildRollbackScript(request, application.ObjectId, servicePrincipalId, thumbprint),
                steps);
        }
        catch (Exception ex)
        {
            steps.Add(new ProvisioningStep("Provisioning", "failed", ex.Message));

            if (createdApplicationId is not null)
            {
                steps.Add(new ProvisioningStep("Grant admin consent manually", "action required",
                    $"An administrator with Privileged Role Administrator or Global Administrator can open {ConsentLink(request, createdApplicationId)} to grant consent, then validate the connection."));
            }

            await SafeAuditAsync(request.Operator, "AuthProvisioningFailed", request.TenantId,
                ex.GetType().Name, cancellationToken).ConfigureAwait(false);

            return new ProvisioningResult(
                false,
                $"Provisioning failed: {ex.Message}",
                null, null, null, null, null,
                BuildRollbackScript(request, "<application-object-id>", "<service-principal-id>", "<thumbprint>"),
                steps);
        }
    }

    private static string ConsentLink(ProvisioningRequest request, string applicationId) =>
        AdminConsentLink.Build(request.BootstrapAuth.Cloud, request.TenantId, applicationId);

    public string BuildRollbackScript(
        ProvisioningRequest request,
        string objectId,
        string servicePrincipalId,
        string? thumbprint)
    {
        return $$"""
            # Purview Governance Copilot — authentication rollback
            # Review before running. Removes the application, service principal and local certificate.
            # Requires Microsoft Graph PowerShell: Install-Module Microsoft.Graph -Scope CurrentUser
            Connect-MgGraph -Scopes 'Application.ReadWrite.All', 'Directory.ReadWrite.All'

            Remove-MgServicePrincipal -ServicePrincipalId '{{servicePrincipalId}}' -ErrorAction SilentlyContinue
            Remove-MgApplication -ApplicationId '{{objectId}}' -ErrorAction SilentlyContinue

            $thumbprint = '{{thumbprint ?? "<thumbprint>"}}'
            if ($thumbprint -notlike '<*') {
                Remove-Item -Path "Cert:\CurrentUser\My\$thumbprint" -Force -ErrorAction SilentlyContinue
            }

            Write-Host 'Rollback complete. Revoke any client secret from the application if it was not deleted.'
            """;
    }

    private static void Validate(ProvisioningRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId) || !Guid.TryParse(request.TenantId, out _))
            throw new InvalidOperationException("A valid tenant id is required.");

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new InvalidOperationException("An application display name is required.");

        SeparationOfDuties.Validate(request.Operator, request.Approver, request.ApproverRole);

        if (request.Mode == AuthMode.ClientSecret)
        {
            if (request.Environment != ProvisioningEnvironment.Lab)
                throw new InvalidOperationException("Client secret authentication is permitted only for lab, test or development environments.");

            if (!SupportedSecretMonths.Contains(request.SecretMonths))
                throw new InvalidOperationException("Secret lifetime must be 3, 6, 12 or 24 months.");
        }
        else
        {
            CertificateLifecyclePolicy.ValidateLifetime(request.CertificateMonths, request.AcknowledgeLongLivedCertificate);
        }

        if (!Guid.TryParse(request.BootstrapAuth.ClientId, out _) ||
            !Guid.TryParse(request.BootstrapAuth.TenantId, out _))
            throw new InvalidOperationException("The bootstrap connection needs a tenant id and an application (client) id.");
    }

    private async Task SafeAuditAsync(string actor, string action, string target, string detail, CancellationToken cancellationToken)
    {
        await _audit.WriteAsync(new AuditEntry(
            Guid.NewGuid(), _clock.UtcNow, string.IsNullOrWhiteSpace(actor) ? "unknown" : actor,
            action, target, detail, null), cancellationToken).ConfigureAwait(false);
    }
}
