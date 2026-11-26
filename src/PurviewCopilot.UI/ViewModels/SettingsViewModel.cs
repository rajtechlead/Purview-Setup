using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Ai;
using PurviewCopilot.Application.Auth;
using PurviewCopilot.Application.Updates;
using PurviewCopilot.Application.Workspace;
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

    public AiProviderDescriptor Descriptor { get; private set; }
    public string DisplayName => Descriptor.DisplayName;
    public string Kind => Descriptor.Kind.ToString();
    public bool IsLocal => Descriptor.IsLocal;
    public string Endpoint => Descriptor.Endpoint;
    public string Model => Descriptor.Model;
    public string PriorityText => $"#{Descriptor.Priority}";

    public void UpdatePriority(int priority)
    {
        Descriptor = Descriptor with { Priority = priority };
        Raise(nameof(PriorityText));
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }
}

public sealed class SettingsViewModel : PageViewModel
{
    private readonly GovernanceWorkspace _workspace;
    private readonly ISettingsStore _settingsStore;
    private readonly ICertificateService _certificates;
    private readonly ConnectionVerifier _verifier;
    private readonly IAuditLog _auditLog;
    private readonly UpdateChecker _updates;
    private readonly Func<string, AuthConfiguration, IKeyVaultCertificateStore> _keyVaultFactory;
    private readonly AuthenticationProvisioningService _authProvisioning;
    private string _provisionRollback = string.Empty;
    private AppSettings _settings = AppSettings.CreateDefault();
    private UpdateManifest? _updateManifest;

    private string _tenantId = string.Empty;
    private string _clientId = string.Empty;
    private string _thumbprint = string.Empty;
    private string _clientSecret = string.Empty;
    private AuthMode _authMode = AuthMode.Certificate;
    private CloudEnvironment _cloud = CloudEnvironment.Commercial;
    private AiRedactionMode _redactionMode = AiRedactionMode.Tokenize;
    private bool _cloudAiEnabled = true;
    private bool _offlineMode;
    private bool _requirePromptReview = true;
    private string _certSubject = WindowsCertificateService.DefaultSubject;
    private int _certMonths = 12;
    private string _pfxPassword = string.Empty;
    private string _status = "Settings loaded.";
    private string _auditStatus = "Audit chain not verified in this session.";
    private string _tenantDisplayName = string.Empty;
    private string _tenantAccessText = $"{Environment.UserName}=Administrator";
    private string _licensePricesText = string.Empty;
    private string _updateManifestUrl = string.Empty;
    private string _updateStatus = "No update check has run.";
    private string _keyVaultUri = string.Empty;
    private string _keyVaultSecretName = "purview-copilot-cert";
    private string _keyVaultStatus = "Key Vault is optional; the Windows store remains the default.";
    private TenantProfile? _selectedTenant;
    private bool _busy;
    private string _provisionDisplayName = "Purview Governance Copilot";
    private string _provisionOwners = string.Empty;
    private string _provisionApprover = string.Empty;
    private string _provisionApproverRole = "Change Manager";
    private CertificateAlgorithm _provisionAlgorithm = CertificateAlgorithm.Rsa4096;
    private int _provisionMonths = 12;
    private int _provisionSecretMonths = 12;
    private ProvisioningEnvironment _provisionEnvironment = ProvisioningEnvironment.Production;
    private bool _provisionDryRun = true;
    private bool _provisionAcknowledge;
    private string _provisionStatus = "No provisioning run in this session.";
    private string _provisionSteps = string.Empty;

    public SettingsViewModel(
        GovernanceWorkspace workspace,
        ISettingsStore settingsStore,
        ICertificateService certificates,
        ConnectionVerifier verifier,
        IAuditLog auditLog,
        UpdateChecker updates,
        Func<string, AuthConfiguration, IKeyVaultCertificateStore> keyVaultFactory,
        AuthenticationProvisioningService authProvisioning)
    {
        _workspace = workspace;
        _settingsStore = settingsStore;
        _certificates = certificates;
        _verifier = verifier;
        _auditLog = auditLog;
        _updates = updates;
        _keyVaultFactory = keyVaultFactory;
        _authProvisioning = authProvisioning;

        ProvisionCommand = new AsyncRelayCommand(ProvisionAsync, () => !Busy);
        SaveProvisionRollbackCommand = new RelayCommand(SaveProvisionRollback);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !Busy);
        CreateCertificateCommand = new RelayCommand(CreateCertificate);
        ImportCertificateCommand = new RelayCommand(ImportCertificate);
        ExportCertificateCommand = new RelayCommand(ExportCertificate);
        RemoveCertificateCommand = new RelayCommand(RemoveCertificate);
        RotateCertificateCommand = new RelayCommand(RotateCertificate);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !Busy);
        RefreshCertificatesCommand = new RelayCommand(LoadCertificates);
        VerifyAuditCommand = new AsyncRelayCommand(VerifyAuditAsync, () => !Busy);
        SaveTenantCommand = new AsyncRelayCommand(SaveTenantAsync, () => !Busy);
        ActivateTenantCommand = new AsyncRelayCommand(ActivateTenantAsync, () => !Busy && SelectedTenant is not null);
        MoveProviderUpCommand = new RelayCommand(p => MoveProvider(p as AiProviderRow, -1));
        MoveProviderDownCommand = new RelayCommand(p => MoveProvider(p as AiProviderRow, 1));
        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync, () => !Busy);
        DownloadUpdateCommand = new AsyncRelayCommand(DownloadUpdateAsync, () => !Busy && _updateManifest is not null);
        UploadKeyVaultCommand = new AsyncRelayCommand(UploadKeyVaultAsync, () => !Busy);
        DownloadKeyVaultCommand = new AsyncRelayCommand(DownloadKeyVaultAsync, () => !Busy);
    }

    public override string Title => "Settings";
    public override string Subtitle => "Connection, certificate lifecycle, AI providers and privacy guardrails.";

    public IReadOnlyList<AuthMode> AuthModes { get; } = Enum.GetValues<AuthMode>();
    public IReadOnlyList<CloudEnvironment> CloudEnvironments { get; } = Enum.GetValues<CloudEnvironment>();
    public IReadOnlyList<CertificateAlgorithm> CertificateAlgorithms { get; } = Enum.GetValues<CertificateAlgorithm>();
    public IReadOnlyList<int> CertificateValidityMonths { get; } = AuthenticationProvisioningService.SupportedValidityMonths;
    public IReadOnlyList<int> SecretValidityMonths { get; } = AuthenticationProvisioningService.SupportedSecretMonths;
    public IReadOnlyList<ProvisioningEnvironment> ProvisioningEnvironments { get; } = Enum.GetValues<ProvisioningEnvironment>();
    public IReadOnlyList<string> ApproverRoles { get; } = SeparationOfDuties.RecommendedApproverRoles;
    public IReadOnlyList<AiRedactionMode> RedactionModes { get; } = Enum.GetValues<AiRedactionMode>();

    public ObservableCollection<CertificateInfo> Certificates { get; } = new();
    public ObservableCollection<AiProviderRow> Providers { get; } = new();
    public ObservableCollection<TenantProfile> TenantProfiles { get; } = new();

    public string TenantId { get => _tenantId; set => SetProperty(ref _tenantId, value); }
    public string ClientId { get => _clientId; set => SetProperty(ref _clientId, value); }
    public string Thumbprint { get => _thumbprint; set => SetProperty(ref _thumbprint, value); }
    public string ClientSecret { get => _clientSecret; set => SetProperty(ref _clientSecret, value); }
    public AuthMode AuthMode { get => _authMode; set => SetProperty(ref _authMode, value); }
    public CloudEnvironment Cloud { get => _cloud; set => SetProperty(ref _cloud, value); }
    public AiRedactionMode RedactionMode { get => _redactionMode; set => SetProperty(ref _redactionMode, value); }
    public bool CloudAiEnabled { get => _cloudAiEnabled; set => SetProperty(ref _cloudAiEnabled, value); }
    public bool OfflineMode { get => _offlineMode; set => SetProperty(ref _offlineMode, value); }
    public bool RequirePromptReview { get => _requirePromptReview; set => SetProperty(ref _requirePromptReview, value); }
    public string CertificateSubject { get => _certSubject; set => SetProperty(ref _certSubject, value); }
    public int CertificateMonths { get => _certMonths; set => SetProperty(ref _certMonths, value); }
    public string PfxPassword { get => _pfxPassword; set => SetProperty(ref _pfxPassword, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string AuditStatus { get => _auditStatus; private set => SetProperty(ref _auditStatus, value); }
    public string TenantDisplayName { get => _tenantDisplayName; set => SetProperty(ref _tenantDisplayName, value); }
    public string TenantAccessText { get => _tenantAccessText; set => SetProperty(ref _tenantAccessText, value); }
    public string LicensePricesText { get => _licensePricesText; set => SetProperty(ref _licensePricesText, value); }
    public string UpdateManifestUrl { get => _updateManifestUrl; set => SetProperty(ref _updateManifestUrl, value); }
    public string UpdateStatus { get => _updateStatus; private set => SetProperty(ref _updateStatus, value); }
    public string KeyVaultUri { get => _keyVaultUri; set => SetProperty(ref _keyVaultUri, value); }
    public string KeyVaultSecretName { get => _keyVaultSecretName; set => SetProperty(ref _keyVaultSecretName, value); }
    public string KeyVaultStatus { get => _keyVaultStatus; private set => SetProperty(ref _keyVaultStatus, value); }
    public bool Busy { get => _busy; private set => SetProperty(ref _busy, value); }

    public string ProvisionDisplayName { get => _provisionDisplayName; set => SetProperty(ref _provisionDisplayName, value); }
    public string ProvisionOwners { get => _provisionOwners; set => SetProperty(ref _provisionOwners, value); }
    public string ProvisionApprover { get => _provisionApprover; set => SetProperty(ref _provisionApprover, value); }
    public string ProvisionApproverRole { get => _provisionApproverRole; set => SetProperty(ref _provisionApproverRole, value); }
    public CertificateAlgorithm ProvisionAlgorithm { get => _provisionAlgorithm; set => SetProperty(ref _provisionAlgorithm, value); }
    public int ProvisionMonths { get => _provisionMonths; set => SetProperty(ref _provisionMonths, value); }
    public int ProvisionSecretMonths { get => _provisionSecretMonths; set => SetProperty(ref _provisionSecretMonths, value); }
    public ProvisioningEnvironment ProvisionEnvironment { get => _provisionEnvironment; set => SetProperty(ref _provisionEnvironment, value); }
    public bool ProvisionDryRun { get => _provisionDryRun; set => SetProperty(ref _provisionDryRun, value); }
    public bool ProvisionAcknowledge { get => _provisionAcknowledge; set => SetProperty(ref _provisionAcknowledge, value); }
    public string ProvisionStatus { get => _provisionStatus; private set => SetProperty(ref _provisionStatus, value); }
    public string ProvisionSteps { get => _provisionSteps; private set => SetProperty(ref _provisionSteps, value); }

    public TenantProfile? SelectedTenant
    {
        get => _selectedTenant;
        set
        {
            if (SetProperty(ref _selectedTenant, value))
            {
                if (value is not null)
                {
                    TenantAccessText = TenantAccessPolicy.Format(value.Access);
                }

                (ActivateTenantCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

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

    public ICommand ProvisionCommand { get; }
    public ICommand SaveProvisionRollbackCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CreateCertificateCommand { get; }
    public ICommand ImportCertificateCommand { get; }
    public ICommand ExportCertificateCommand { get; }
    public ICommand RemoveCertificateCommand { get; }
    public ICommand RotateCertificateCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand RefreshCertificatesCommand { get; }
    public ICommand VerifyAuditCommand { get; }
    public ICommand SaveTenantCommand { get; }
    public ICommand ActivateTenantCommand { get; }
    public ICommand MoveProviderUpCommand { get; }
    public ICommand MoveProviderDownCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand DownloadUpdateCommand { get; }
    public ICommand UploadKeyVaultCommand { get; }
    public ICommand DownloadKeyVaultCommand { get; }

    public override async Task RefreshAsync()
    {
        _settings = await _settingsStore.LoadAsync().ConfigureAwait(true);

        TenantId = _settings.Auth.TenantId;
        ClientId = _settings.Auth.ClientId;
        Thumbprint = _settings.Auth.CertificateThumbprint ?? string.Empty;
        AuthMode = _settings.Auth.Mode;
        Cloud = _settings.Auth.Cloud;
        RedactionMode = _settings.RedactionMode;
        CloudAiEnabled = _settings.CloudAiEnabled;
        OfflineMode = _settings.OfflineMode;
        RequirePromptReview = _settings.RequirePromptReview;
        UpdateManifestUrl = _settings.UpdateManifestUrl ?? string.Empty;
        LicensePricesText = string.Join("; ", _settings.LicensePrices.Select(p => $"{p.Key}={p.Value.ToString(CultureInfo.InvariantCulture)}"));
        KeyVaultUri = _settings.KeyVaultUri ?? string.Empty;
        KeyVaultSecretName = string.IsNullOrWhiteSpace(_settings.KeyVaultSecretName) ? "purview-copilot-cert" : _settings.KeyVaultSecretName;

        Providers.Clear();
        foreach (var provider in AiProviderSequence.Normalize(_settings.Providers))
        {
            Providers.Add(new AiProviderRow(provider));
        }

        LoadCertificates();

        var profiles = await _workspace.LoadTenantProfilesAsync().ConfigureAwait(true);
        TenantProfiles.Clear();
        foreach (var profile in profiles)
        {
            TenantProfiles.Add(profile);
        }

        SelectedTenant ??= TenantProfiles.FirstOrDefault();
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
            _settings.Auth = BuildAuth();

            _settings.RedactionMode = RedactionMode;
            _settings.CloudAiEnabled = CloudAiEnabled;
            _settings.OfflineMode = OfflineMode;
            _settings.RequirePromptReview = RequirePromptReview;
            _settings.UpdateManifestUrl = string.IsNullOrWhiteSpace(UpdateManifestUrl) ? null : UpdateManifestUrl.Trim();
            _settings.LicensePrices = ParseLicensePrices(LicensePricesText);
            _settings.KeyVaultUri = string.IsNullOrWhiteSpace(KeyVaultUri) ? null : KeyVaultUri.Trim();
            _settings.KeyVaultSecretName = string.IsNullOrWhiteSpace(KeyVaultSecretName) ? null : KeyVaultSecretName.Trim();

            _settings.Providers = AiProviderSequence
                .Normalize(Providers.Select(p => p.Descriptor with { Enabled = p.Enabled }))
                .ToList();

            await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
            Status = "Settings saved. The lab client secret is protected with Windows DPAPI; AI provider keys are read from environment variables only.";
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

    private async Task ProvisionAsync()
    {
        if (AuthMode is not (AuthMode.Certificate or AuthMode.ClientSecret))
        {
            ProvisionStatus = "Select certificate or client-secret authentication before provisioning.";
            return;
        }

        if (ProvisionDryRun)
        {
            ProvisionStatus = "Dry run selected. Checking the request without changing the tenant...";
        }

        Busy = true;
        try
        {
            var owners = ProvisionOwners
                .Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var result = await _authProvisioning.ProvisionAsync(new ProvisioningRequest(
                TenantId.Trim(),
                ProvisionDisplayName.Trim(),
                AuthMode,
                ProvisionAlgorithm,
                ProvisionMonths,
                ProvisionSecretMonths,
                ProvisionEnvironment,
                owners,
                ProvisionDryRun,
                ProvisionAcknowledge,
                BuildAuth(),
                Environment.UserName,
                ProvisionApprover.Trim(),
                ProvisionApproverRole)).ConfigureAwait(true);

            _provisionRollback = result.RollbackScript;
            ProvisionSteps = string.Join(Environment.NewLine,
                result.Steps.Select(s => $"{s.Status.ToUpperInvariant(),-8} {s.Name}: {s.Detail}"));
            ProvisionStatus = result.Summary;

            if (result.Succeeded && result.Auth is not null)
            {
                AuthMode = result.Auth.Mode;
                ClientId = result.Auth.ClientId;
                Thumbprint = result.Auth.CertificateThumbprint ?? string.Empty;
                ClientSecret = result.Auth.ClientSecret ?? string.Empty;
                Cloud = result.Auth.Cloud;

                _settings.Auth = result.Auth;
                await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);

                try
                {
                    await _workspace.SaveTenantAsync(ProvisionDisplayName, result.Auth, Environment.UserName).ConfigureAwait(true);
                    ProvisionStatus += " Settings saved and the tenant registered.";
                }
                catch (Exception ex)
                {
                    ProvisionStatus += $" Settings saved, but the tenant profile was not: {ex.Message}";
                }

                LoadCertificates();
            }
        }
        catch (Exception ex)
        {
            ProvisionStatus = $"Provisioning failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private void SaveProvisionRollback()
    {
        if (string.IsNullOrWhiteSpace(_provisionRollback))
        {
            ProvisionStatus = "Run provisioning first to generate a rollback script.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PowerShell script (*.ps1)|*.ps1",
            FileName = "purview-authentication-rollback.ps1"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, _provisionRollback);
            ProvisionStatus = $"Rollback script saved to {dialog.FileName}. It is destructive; review it first.";
        }
        catch (Exception ex)
        {
            ProvisionStatus = $"Rollback script save failed: {ex.Message}";
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

    private async Task UploadKeyVaultAsync()
    {
        if (string.IsNullOrWhiteSpace(Thumbprint))
        {
            KeyVaultStatus = "Select or create a certificate first.";
            return;
        }

        Busy = true;
        var temp = Path.Combine(Path.GetTempPath(), $"pgc-vault-{Guid.NewGuid():N}.pfx");

        try
        {
            var store = _keyVaultFactory(KeyVaultUri.Trim(), BuildAuth());
            var password = string.IsNullOrEmpty(PfxPassword) ? null : PfxPassword;

            _certificates.ExportToPfx(Thumbprint, temp, password);
            var bytes = await File.ReadAllBytesAsync(temp).ConfigureAwait(true);
            await store.UploadPfxBase64Async(KeyVaultSecretName.Trim(), Convert.ToBase64String(bytes)).ConfigureAwait(true);

            KeyVaultStatus = $"Uploaded {Thumbprint} to {KeyVaultUri.Trim()} secret '{KeyVaultSecretName.Trim()}'. The vault must grant this app secret get/set permission.";
        }
        catch (Exception ex)
        {
            KeyVaultStatus = $"Key Vault upload failed: {ex.Message}";
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            Busy = false;
        }
    }

    private async Task DownloadKeyVaultAsync()
    {
        Busy = true;
        var temp = Path.Combine(Path.GetTempPath(), $"pgc-vault-{Guid.NewGuid():N}.pfx");

        try
        {
            var store = _keyVaultFactory(KeyVaultUri.Trim(), BuildAuth());
            var base64 = await store.DownloadPfxBase64Async(KeyVaultSecretName.Trim()).ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(base64))
            {
                KeyVaultStatus = $"Secret '{KeyVaultSecretName.Trim()}' was not found in {KeyVaultUri.Trim()}.";
                return;
            }

            await File.WriteAllBytesAsync(temp, Convert.FromBase64String(base64)).ConfigureAwait(true);
            var password = string.IsNullOrEmpty(PfxPassword) ? null : PfxPassword;
            var info = _certificates.ImportFromPfx(temp, password, installToStore: true);

            Thumbprint = info.Thumbprint;
            LoadCertificates();
            KeyVaultStatus = $"Imported {info.Thumbprint} from {KeyVaultUri.Trim()}. Register it on the app registration before testing the connection.";
        }
        catch (Exception ex)
        {
            KeyVaultStatus = $"Key Vault import failed: {ex.Message}";
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            Busy = false;
        }
    }

    private async Task CheckUpdatesAsync()
    {
        Busy = true;
        try
        {
            var current = typeof(SettingsViewModel).Assembly.GetName().Version ?? new Version(1, 0, 0);
            var result = await _updates.CheckAsync(UpdateManifestUrl, current).ConfigureAwait(true);

            _updateManifest = result.UpdateAvailable ? result.Manifest : null;
            UpdateStatus = result.UpdateAvailable
                ? $"{result.Message} Download it, verify the hash shown by your distribution channel, then run the MSI."
                : result.Message;
        }
        catch (Exception ex)
        {
            _updateManifest = null;
            UpdateStatus = $"Update check failed: {ex.Message}";
        }
        finally
        {
            (DownloadUpdateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            Busy = false;
        }
    }

    private async Task DownloadUpdateAsync()
    {
        if (_updateManifest is null) return;

        Busy = true;
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PurviewGovernanceCopilot",
                "updates");

            var path = await _updates.DownloadAsync(_updateManifest, directory).ConfigureAwait(true);
            UpdateStatus = $"Update verified (SHA-256) and saved to {path}. Run the MSI to upgrade; code signing is still recommended before enterprise rollout.";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Update download failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private void MoveProvider(AiProviderRow? row, int offset)
    {
        if (row is null) return;

        var current = Providers.Select(p => p.Descriptor with { Enabled = p.Enabled }).ToList();
        var moved = AiProviderSequence.Move(current, row.Descriptor.Id, offset);

        Providers.Clear();
        foreach (var descriptor in moved)
        {
            Providers.Add(new AiProviderRow(descriptor));
        }

        Status = "Auto-switch order updated. Save settings to apply it; providers are tried top to bottom.";
    }

    private static Dictionary<string, double> ParseLicensePrices(string text)
    {
        var prices = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in text.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var price) || price < 0)
            {
                throw new InvalidOperationException($"Price entry '{entry}' must be SKU=price, for example SPE_E5=57.");
            }

            prices[parts[0]] = price;
        }

        return prices;
    }

    private AuthConfiguration BuildAuth() => new(
        AuthMode,
        TenantId.Trim(),
        ClientId.Trim(),
        string.IsNullOrWhiteSpace(Thumbprint) ? null : Thumbprint.Trim(),
        string.IsNullOrWhiteSpace(ClientSecret) ? null : ClientSecret,
        null,
        Cloud);

    private async Task SaveTenantAsync()
    {
        Busy = true;
        try
        {
            var access = TenantAccessPolicy.ParseAssignments(TenantAccessText, Environment.UserName);
            var profile = await _workspace.SaveTenantAsync(TenantDisplayName, BuildAuth(), Environment.UserName, access)
                .ConfigureAwait(true);

            TenantDisplayName = profile.DisplayName;
            TenantAccessText = TenantAccessPolicy.Format(profile.Access);
            TenantProfiles.Clear();
            foreach (var item in await _workspace.LoadTenantProfilesAsync().ConfigureAwait(true))
            {
                TenantProfiles.Add(item);
            }

            SelectedTenant = TenantProfiles.FirstOrDefault(p => p.TenantId == profile.TenantId) ?? profile;
            Status = $"Customer tenant {profile.DisplayName} saved. Activate it to target it in the next assessment.";
        }
        catch (Exception ex)
        {
            Status = $"Tenant save failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ActivateTenantAsync()
    {
        if (SelectedTenant is null) return;

        Busy = true;
        try
        {
            await _workspace.ActivateTenantAsync(SelectedTenant, Environment.UserName).ConfigureAwait(true);

            TenantId = SelectedTenant.Auth.TenantId;
            ClientId = SelectedTenant.Auth.ClientId;
            Thumbprint = SelectedTenant.Auth.CertificateThumbprint ?? string.Empty;
            AuthMode = SelectedTenant.Auth.Mode;
            Cloud = SelectedTenant.Auth.Cloud;
            ClientSecret = SelectedTenant.Auth.ClientSecret ?? string.Empty;

            Status = $"Activated {SelectedTenant.DisplayName}. The next assessment targets this tenant.";
        }
        catch (Exception ex)
        {
            Status = $"Activation failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task VerifyAuditAsync()
    {
        Busy = true;
        try
        {
            var status = await _auditLog.VerifyAsync().ConfigureAwait(true);
            AuditStatus = status.Message;
        }
        catch (Exception ex)
        {
            AuditStatus = $"Audit verification failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task TestConnectionAsync()
    {
        Busy = true;
        try
        {
            var (ok, message) = await _verifier.VerifyAsync(BuildAuth()).ConfigureAwait(true);
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
