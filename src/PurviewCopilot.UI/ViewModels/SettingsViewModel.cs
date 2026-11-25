using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;
using PurviewCopilot.Infrastructure.Auth;
using PurviewCopilot.UI.Mvvm;

namespace PurviewCopilot.UI.ViewModels;

public sealed class AiProviderRow : ObservableObject
{
    private bool _enabled;

    public AiProviderRow(AiProviderDescriptor descriptor)
    {
        Descriptor = descriptor;
        _enabled = descriptor.Enabled;
    }

    public AiProviderDescriptor Descriptor { get; }
    public string DisplayName => Descriptor.DisplayName;
    public string Kind => Descriptor.Kind.ToString();
    public bool IsLocal => Descriptor.IsLocal;
    public string Endpoint => Descriptor.Endpoint;
    public string Model => Descriptor.Model;

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }
}

public sealed class SettingsViewModel : PageViewModel
{
    private readonly ISettingsStore _settingsStore;
    private readonly ICertificateService _certificates;
    private readonly ConnectionVerifier _verifier;
    private AppSettings _settings = AppSettings.CreateDefault();

    private string _tenantId = string.Empty;
    private string _clientId = string.Empty;
    private string _thumbprint = string.Empty;
    private string _clientSecret = string.Empty;
    private AuthMode _authMode = AuthMode.Certificate;
    private AiRedactionMode _redactionMode = AiRedactionMode.Tokenize;
    private bool _cloudAiEnabled = true;
    private bool _offlineMode;
    private bool _requirePromptReview = true;
    private string _certSubject = WindowsCertificateService.DefaultSubject;
    private int _certMonths = 12;
    private string _pfxPassword = string.Empty;
    private string _status = "Settings loaded.";
    private bool _busy;

    public SettingsViewModel(
        ISettingsStore settingsStore,
        ICertificateService certificates,
        ConnectionVerifier verifier)
    {
        _settingsStore = settingsStore;
        _certificates = certificates;
        _verifier = verifier;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !Busy);
        CreateCertificateCommand = new RelayCommand(CreateCertificate);
        ImportCertificateCommand = new RelayCommand(ImportCertificate);
        ExportCertificateCommand = new RelayCommand(ExportCertificate);
        RemoveCertificateCommand = new RelayCommand(RemoveCertificate);
        RotateCertificateCommand = new RelayCommand(RotateCertificate);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !Busy);
        RefreshCertificatesCommand = new RelayCommand(LoadCertificates);
    }

    public override string Title => "Settings";
    public override string Subtitle => "Connection, certificate lifecycle, AI providers and privacy guardrails.";

    public IReadOnlyList<AuthMode> AuthModes { get; } = Enum.GetValues<AuthMode>();
    public IReadOnlyList<AiRedactionMode> RedactionModes { get; } = Enum.GetValues<AiRedactionMode>();

    public ObservableCollection<CertificateInfo> Certificates { get; } = new();
    public ObservableCollection<AiProviderRow> Providers { get; } = new();

    public string TenantId { get => _tenantId; set => SetProperty(ref _tenantId, value); }
    public string ClientId { get => _clientId; set => SetProperty(ref _clientId, value); }
    public string Thumbprint { get => _thumbprint; set => SetProperty(ref _thumbprint, value); }
    public string ClientSecret { get => _clientSecret; set => SetProperty(ref _clientSecret, value); }
    public AuthMode AuthMode { get => _authMode; set => SetProperty(ref _authMode, value); }
    public AiRedactionMode RedactionMode { get => _redactionMode; set => SetProperty(ref _redactionMode, value); }
    public bool CloudAiEnabled { get => _cloudAiEnabled; set => SetProperty(ref _cloudAiEnabled, value); }
    public bool OfflineMode { get => _offlineMode; set => SetProperty(ref _offlineMode, value); }
    public bool RequirePromptReview { get => _requirePromptReview; set => SetProperty(ref _requirePromptReview, value); }
    public string CertificateSubject { get => _certSubject; set => SetProperty(ref _certSubject, value); }
    public int CertificateMonths { get => _certMonths; set => SetProperty(ref _certMonths, value); }
    public string PfxPassword { get => _pfxPassword; set => SetProperty(ref _pfxPassword, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool Busy { get => _busy; private set => SetProperty(ref _busy, value); }

    public string CertificateWarning
    {
        get
        {
            var expiring = Certificates.Where(c => c.IsExpiringSoon(30)).ToList();
            return expiring.Count == 0
                ? "No certificate expires within 30 days."
                : $"{expiring.Count} certificate(s) expire within 30 days: {string.Join(", ", expiring.Select(c => c.NotAfter.ToString("yyyy-MM-dd")))}";
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand CreateCertificateCommand { get; }
    public ICommand ImportCertificateCommand { get; }
    public ICommand ExportCertificateCommand { get; }
    public ICommand RemoveCertificateCommand { get; }
    public ICommand RotateCertificateCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand RefreshCertificatesCommand { get; }

    public override async Task RefreshAsync()
    {
        _settings = await _settingsStore.LoadAsync().ConfigureAwait(true);

        TenantId = _settings.Auth.TenantId;
        ClientId = _settings.Auth.ClientId;
        Thumbprint = _settings.Auth.CertificateThumbprint ?? string.Empty;
        AuthMode = _settings.Auth.Mode;
        RedactionMode = _settings.RedactionMode;
        CloudAiEnabled = _settings.CloudAiEnabled;
        OfflineMode = _settings.OfflineMode;
        RequirePromptReview = _settings.RequirePromptReview;

        Providers.Clear();
        foreach (var provider in _settings.Providers.OrderBy(p => p.Priority))
        {
            Providers.Add(new AiProviderRow(provider));
        }

        LoadCertificates();
    }

    private void LoadCertificates()
    {
        Certificates.Clear();
        foreach (var certificate in _certificates.List())
        {
            Certificates.Add(certificate);
        }

        Raise(nameof(CertificateWarning));
    }

    private async Task SaveAsync()
    {
        Busy = true;
        try
        {
            _settings.Auth = new AuthConfiguration(
                AuthMode,
                TenantId.Trim(),
                ClientId.Trim(),
                string.IsNullOrWhiteSpace(Thumbprint) ? null : Thumbprint.Trim(),
                string.IsNullOrWhiteSpace(ClientSecret) ? null : ClientSecret,
                null);

            _settings.RedactionMode = RedactionMode;
            _settings.CloudAiEnabled = CloudAiEnabled;
            _settings.OfflineMode = OfflineMode;
            _settings.RequirePromptReview = RequirePromptReview;

            _settings.Providers = Providers.Select(p => p.Descriptor with { Enabled = p.Enabled }).ToList();

            await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
            Status = "Settings saved. Credentials are stored locally; secrets are read from environment variables only.";
        }
        catch (Exception ex)
        {
            Status = $"Save failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private void CreateCertificate()
    {
        try
        {
            var info = _certificates.CreateSelfSigned(CertificateSubject, CertificateMonths, installToStore: true);
            Thumbprint = info.Thumbprint;
            LoadCertificates();
            Status = $"Created certificate {info.Thumbprint} valid until {info.NotAfter:yyyy-MM-dd}. Registered to CurrentUser\\My.";
        }
        catch (Exception ex)
        {
            Status = $"Certificate creation failed: {ex.Message}";
        }
    }

    private void ImportCertificate()
    {
        var dialog = new OpenFileDialog { Filter = "PFX certificate (*.pfx)|*.pfx|All files|*.*" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var info = _certificates.ImportFromPfx(dialog.FileName, string.IsNullOrEmpty(PfxPassword) ? null : PfxPassword, true);
            Thumbprint = info.Thumbprint;
            LoadCertificates();
            Status = $"Imported {info.Thumbprint}.";
        }
        catch (Exception ex)
        {
            Status = $"Import failed: {ex.Message}";
        }
    }

    private void ExportCertificate()
    {
        if (string.IsNullOrWhiteSpace(Thumbprint))
        {
            Status = "Select or create a certificate first.";
            return;
        }

        var dialog = new SaveFileDialog { Filter = "PFX certificate (*.pfx)|*.pfx", FileName = "purview-copilot.pfx" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            _certificates.ExportToPfx(Thumbprint, dialog.FileName, string.IsNullOrEmpty(PfxPassword) ? null : PfxPassword);
            Status = $"Exported to {dialog.FileName}. Protect this file — it contains a private key.";
        }
        catch (Exception ex)
        {
            Status = $"Export failed: {ex.Message}";
        }
    }

    private void RemoveCertificate()
    {
        if (string.IsNullOrWhiteSpace(Thumbprint)) return;

        try
        {
            _certificates.Remove(Thumbprint);
            Status = $"Removed {Thumbprint} from CurrentUser\\My.";
            LoadCertificates();
        }
        catch (Exception ex)
        {
            Status = $"Removal failed: {ex.Message}";
        }
    }

    private void RotateCertificate()
    {
        try
        {
            var info = _certificates.Rotate(CertificateSubject, CertificateMonths, true);
            Thumbprint = info.Thumbprint;
            LoadCertificates();
            Status = $"Rotated. New thumbprint {info.Thumbprint}. Upload it to the app registration, then remove the old certificate.";
        }
        catch (Exception ex)
        {
            Status = $"Rotation failed: {ex.Message}";
        }
    }

    private async Task TestConnectionAsync()
    {
        Busy = true;
        try
        {
            var auth = new AuthConfiguration(AuthMode, TenantId.Trim(), ClientId.Trim(),
                string.IsNullOrWhiteSpace(Thumbprint) ? null : Thumbprint.Trim(),
                string.IsNullOrWhiteSpace(ClientSecret) ? null : ClientSecret, null);

            var (ok, message) = await _verifier.VerifyAsync(auth).ConfigureAwait(true);
            Status = message;
            if (!ok && AuthMode == AuthMode.ClientSecret)
            {
                Status += " Client secret mode is for lab use only.";
            }
        }
        finally
        {
            Busy = false;
        }
    }
}
