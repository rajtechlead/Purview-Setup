using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Infrastructure.Auth;

/// <summary>
/// Certificate lifecycle for Entra app-only authentication: create, import, export, install,
/// rotate and remove. Private keys are marked non-exportable only when the caller does not need
/// portability; PFX export requires an exportable key, so creation keeps the key exportable.
/// </summary>
public sealed class WindowsCertificateService : ICertificateService
{
    public const string DefaultSubject = "CN=Purview Governance Copilot";
    private const string AppSubjectMarker = "Purview Governance Copilot";

    public CertificateInfo CreateSelfSigned(string subject, int validMonths, bool installToStore)
    {
        if (validMonths is < 1 or > 36)
            throw new ArgumentOutOfRangeException(nameof(validMonths), "Validity must be between 1 and 36 months.");

        using var rsa = RSA.Create(4096);
        var request = new CertificateRequest(
            string.IsNullOrWhiteSpace(subject) ? DefaultSubject : subject,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") }, critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddMonths(validMonths));

        if (installToStore)
        {
            InstallCertificate(certificate);
        }

        return ToInfo(certificate, installToStore ? @"CurrentUser\My" : "Ephemeral");
    }

    public CertificateInfo ImportFromPfx(string path, string? password, bool installToStore)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("PFX file not found.", path);

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            path,
            password,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);

        if (installToStore) InstallCertificate(certificate);

        var info = ToInfo(certificate, installToStore ? @"CurrentUser\My" : path);
        certificate.Dispose();
        return info;
    }

    public void ExportToPfx(string thumbprint, string path, string? password)
    {
        using var certificate = RequireWithPrivateKey(thumbprint);
        var bytes = certificate.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(path, bytes);
    }

    public void Install(string thumbprint)
    {
        using var certificate = RequireWithPrivateKey(thumbprint);
        InstallCertificate(certificate);
    }

    public void Remove(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, Normalize(thumbprint), validOnly: false);

        foreach (var certificate in matches)
        {
            store.Remove(certificate);
        }
    }

    public CertificateInfo? FindByThumbprint(string thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint)) return null;

        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            try
            {
                store.Open(OpenFlags.ReadOnly);
            }
            catch (CryptographicException)
            {
                continue;
            }

            var match = store.Certificates
                .Find(X509FindType.FindByThumbprint, Normalize(thumbprint), validOnly: false)
                .Cast<X509Certificate2>()
                .FirstOrDefault();

            if (match is not null)
            {
                return ToInfo(match, $"{location}\\My");
            }
        }

        return null;
    }

    /// <summary>Lists only certificates created by this application — never the whole user store.</summary>
    public IReadOnlyList<CertificateInfo> List()
    {
        var results = new List<CertificateInfo>();

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        foreach (var certificate in store.Certificates.Cast<X509Certificate2>())
        {
            if (!certificate.Subject.Contains(AppSubjectMarker, StringComparison.OrdinalIgnoreCase)) continue;
            results.Add(ToInfo(certificate, @"CurrentUser\My"));
        }

        return results.OrderByDescending(c => c.NotAfter).ToList();
    }

    public CertificateInfo Rotate(string subject, int validMonths, bool installToStore)
    {
        // Rotation creates the replacement first so authentication never has a gap.
        return CreateSelfSigned(subject, validMonths, installToStore);
    }

    public X509Certificate2 LoadForAuthentication(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        var match = store.Certificates
            .Find(X509FindType.FindByThumbprint, Normalize(thumbprint), validOnly: false)
            .Cast<X509Certificate2>()
            .FirstOrDefault();

        if (match is null)
        {
            throw new InvalidOperationException($"Certificate {thumbprint} was not found in CurrentUser\\My.");
        }

        if (!match.HasPrivateKey)
        {
            throw new InvalidOperationException("The certificate has no private key and cannot be used for authentication.");
        }

        return match;
    }

    private static void InstallCertificate(X509Certificate2 certificate)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);

        var withoutPrivateKey = X509CertificateLoader.LoadCertificate(certificate.RawData);
        var withKey = withoutPrivateKey.CopyWithPrivateKey(certificate.GetRSAPrivateKey()!);
        store.Add(withKey);
    }

    private X509Certificate2 RequireWithPrivateKey(string thumbprint) => LoadForAuthentication(thumbprint);

    private static CertificateInfo ToInfo(X509Certificate2 certificate, string location) =>
        new(
            certificate.Thumbprint,
            certificate.Subject,
            certificate.Issuer,
            certificate.NotBefore,
            certificate.NotAfter,
            certificate.HasPrivateKey,
            location);

    private static string Normalize(string thumbprint) =>
        thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}
