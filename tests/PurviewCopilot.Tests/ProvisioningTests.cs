using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Auth;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class ProvisioningTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string Client = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task DryRun_PlansEveryStep_AndChangesNothing()
    {
        var gateway = new FakeGateway();
        var service = new AuthenticationProvisioningService(gateway, new FakeCertificates(), new FakeAudit(), new FakeClock());

        var result = await service.ProvisionAsync(Request(dryRun: true));

        Assert.True(result.Succeeded);
        Assert.Null(result.ApplicationId);
        Assert.Contains(result.Steps, s => s.Name == "Create application" && s.Status == "planned");
        Assert.Contains("Remove-MgApplication", result.RollbackScript, StringComparison.Ordinal);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task CertificateProvisioning_CreatesAppServicePrincipalAndValidates()
    {
        var gateway = new FakeGateway();
        var service = new AuthenticationProvisioningService(gateway, new FakeCertificates(), new FakeAudit(), new FakeClock());

        var result = await service.ProvisionAsync(Request());

        Assert.True(result.Succeeded);
        Assert.Equal("app-id", result.ApplicationId);
        Assert.Equal("sp-id", result.ServicePrincipalId);
        Assert.Equal("THUMB", result.Thumbprint);
        Assert.Equal(AuthMode.Certificate, result.Auth!.Mode);
        Assert.Equal(Tenant, result.Auth.TenantId);
        Assert.Equal(1, gateway.CertificatesAdded);
        Assert.Equal(1, gateway.ConsentsGranted);
        Assert.Contains("Remove-MgServicePrincipal", result.RollbackScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientSecret_IsAllowedOnlyInLab()
    {
        var service = new AuthenticationProvisioningService(new FakeGateway(), new FakeCertificates(), new FakeAudit(), new FakeClock());

        var production = await service.ProvisionAsync(Request(mode: AuthMode.ClientSecret, environment: ProvisioningEnvironment.Production));
        var lab = await service.ProvisionAsync(Request(mode: AuthMode.ClientSecret, environment: ProvisioningEnvironment.Lab));

        Assert.False(production.Succeeded);
        Assert.Contains("lab", production.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.True(lab.Succeeded);
        Assert.Equal("secret-value", lab.Auth!.ClientSecret);
    }

    [Fact]
    public async Task TenYearCertificate_RequiresAcknowledgement()
    {
        var service = new AuthenticationProvisioningService(new FakeGateway(), new FakeCertificates(), new FakeAudit(), new FakeClock());

        var result = await service.ProvisionAsync(Request(months: 120, acknowledge: false));

        Assert.False(result.Succeeded);
        Assert.Contains("acknowledgement", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("alice", "Change Manager")]
    [InlineData("bob", "Global Administrator")]
    public async Task SeparationOfDuties_BlocksSelfApprovalAndAdminRoles(string approver, string role)
    {
        var service = new AuthenticationProvisioningService(new FakeGateway(), new FakeCertificates(), new FakeAudit(), new FakeClock());

        var result = await service.ProvisionAsync(Request(approver: approver, approverRole: role));

        Assert.False(result.Succeeded);
        Assert.Contains("Separation of duties", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedValidity_IsRejected()
    {
        var service = new AuthenticationProvisioningService(new FakeGateway(), new FakeCertificates(), new FakeAudit(), new FakeClock());

        var result = await service.ProvisionAsync(Request(months: 18));

        Assert.False(result.Succeeded);
        Assert.Contains("6, 12, 24, 36, 60 or 120", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void RealCertificateCreation_SupportsRsaAndEcc()
    {
        var certificates = new Infrastructure.Auth.WindowsCertificateService();

        var rsa = certificates.CreateSelfSigned("CN=PGC Test RSA", 6, false, CertificateAlgorithm.Rsa2048);
        var ecc = certificates.CreateSelfSigned("CN=PGC Test ECC", 6, false, CertificateAlgorithm.EccP256);

        Assert.True(rsa.HasPrivateKey);
        Assert.True(ecc.HasPrivateKey);
        Assert.NotEqual(rsa.Thumbprint, ecc.Thumbprint);
    }

    private static ProvisioningRequest Request(
        bool dryRun = false,
        AuthMode mode = AuthMode.Certificate,
        int months = 12,
        bool acknowledge = true,
        string approver = "bob",
        string approverRole = "Change Manager",
        ProvisioningEnvironment environment = ProvisioningEnvironment.Production) =>
        new(
            Tenant,
            "Purview Governance Copilot",
            mode,
            CertificateAlgorithm.Rsa4096,
            months,
            12,
            environment,
            new[] { "owner@contoso.com" },
            dryRun,
            acknowledge,
            new AuthConfiguration(AuthMode.Interactive, Tenant, Client, null, null, null),
            "alice",
            approver,
            approverRole);

    private sealed class FakeGateway : IAuthenticationProvisioningGateway
    {
        public int Calls { get; private set; }
        public int CertificatesAdded { get; private set; }
        public int ConsentsGranted { get; private set; }

        public Task<(string ApplicationId, string ObjectId)> CreateApplicationAsync(string displayName, AuthConfiguration bootstrap, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(("app-id", "object-id"));
        }

        public Task<string> CreateServicePrincipalAsync(string applicationId, AuthConfiguration bootstrap, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult("sp-id");
        }

        public Task AddCertificateAsync(string objectId, string publicCertificateBase64, AuthConfiguration bootstrap, CancellationToken cancellationToken = default)
        {
            CertificatesAdded++;
            return Task.CompletedTask;
        }

        public Task<string> AddSecretAsync(string objectId, string displayName, int months, AuthConfiguration bootstrap, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult("secret-value");
        }

        public Task AddOwnerAsync(string objectId, string owner, AuthConfiguration bootstrap, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }

        public Task GrantGraphAdminConsentAsync(string servicePrincipalId, AuthConfiguration bootstrap, CancellationToken cancellationToken = default)
        {
            ConsentsGranted++;
            return Task.CompletedTask;
        }

        public Task<(bool Ok, string Message)> ValidateAsync(AuthConfiguration auth, CancellationToken cancellationToken = default) =>
            Task.FromResult((true, "Validated."));

        public Task DeleteApplicationAsync(string objectId, string servicePrincipalId, AuthConfiguration bootstrap, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCertificates : ICertificateService
    {
        public CertificateInfo CreateSelfSigned(string subject, int validMonths, bool installToStore) =>
            CreateSelfSigned(subject, validMonths, installToStore, CertificateAlgorithm.Rsa4096);

        public CertificateInfo CreateSelfSigned(string subject, int validMonths, bool installToStore, CertificateAlgorithm algorithm) =>
            new("THUMB", subject, "CN=Test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(validMonths), true, "Test");

        public string ExportPublicDerBase64(string thumbprint) => "base64-cert";

        public CertificateInfo ImportFromPfx(string path, string? password, bool installToStore) => throw new NotSupportedException();
        public void ExportToPfx(string thumbprint, string path, string? password) => throw new NotSupportedException();
        public void Install(string thumbprint) => throw new NotSupportedException();
        public void Remove(string thumbprint) => throw new NotSupportedException();
        public CertificateInfo? FindByThumbprint(string thumbprint) => null;
        public IReadOnlyList<CertificateInfo> List() => Array.Empty<CertificateInfo>();
        public CertificateInfo Rotate(string subject, int validMonths, bool installToStore) => throw new NotSupportedException();
    }

    private sealed class FakeAudit : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = new();

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEntry>> ReadAsync(int take = 200, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditEntry>>(Entries);

        public Task<AuditChainStatus> VerifyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuditChainStatus(true, Entries.Count, null, "ok"));
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
