using System.Collections.ObjectModel;
using System.Windows.Input;
using PurviewCopilot.Application.Licensing;
using PurviewCopilot.Application.Workspace;
using PurviewCopilot.Domain;
using PurviewCopilot.UI.Mvvm;

namespace PurviewCopilot.UI.ViewModels;

public sealed class MonitoringViewModel : PageViewModel
{
    private readonly GovernanceWorkspace _workspace;
    private string _status = "Run an assessment to establish a baseline.";

    public MonitoringViewModel(GovernanceWorkspace workspace)
    {
        _workspace = workspace;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public override string Title => "Monitoring";
    public override string Subtitle => "Configuration drift, licence utilisation and continuous governance signals.";

    public ObservableCollection<DriftItem> Drift { get; } = new();
    public ObservableCollection<LicenseInsight> LicenseInsights { get; } = new();
    public ObservableCollection<LicenseSkuRow> Licenses { get; } = new();

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public ICommand RefreshCommand { get; }

    public override Task RefreshAsync()
    {
        Drift.Clear();
        LicenseInsights.Clear();
        Licenses.Clear();

        foreach (var item in _workspace.Drift)
        {
            Drift.Add(item);
        }

        foreach (var insight in _workspace.LicenseInsights)
        {
            LicenseInsights.Add(insight);
        }

        if (_workspace.Tenant is not null)
        {
            foreach (var sku in _workspace.Tenant.Licenses.OrderByDescending(l => l.Consumed))
            {
                Licenses.Add(new LicenseSkuRow(sku.PartNumber, sku.Consumed, sku.Enabled, LicenseOptimizer.UtilizationPercent(sku)));
            }
        }

        Status = Drift.Count == 0
            ? "No drift detected against the previous collection."
            : $"{Drift.Count} drift item(s) detected since the last assessment.";

        return Task.CompletedTask;
    }
}

public sealed record LicenseSkuRow(string PartNumber, int Consumed, int Enabled, double Utilization)
{
    public string UtilizationText => $"{Utilization:0.#}%";
}
