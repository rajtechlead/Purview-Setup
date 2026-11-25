using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Ai;
using PurviewCopilot.Application.Licensing;
using PurviewCopilot.Application.Monitoring;
using PurviewCopilot.Application.Workspace;
using PurviewCopilot.Domain;
using PurviewCopilot.UI.Mvvm;

namespace PurviewCopilot.UI.ViewModels;

public sealed class MonitoringViewModel : PageViewModel
{
    private readonly GovernanceWorkspace _workspace;
    private readonly ISettingsStore _settings;
    private readonly IAiUsageStore _aiUsage;
    private readonly AlertNotifier _notifier;
    private readonly DispatcherTimer _timer;
    private string _status = "Run an assessment to establish a baseline.";
    private string _scheduleText = "Automatic checks are off.";
    private string _aiUsageText = "No AI requests recorded.";
    private string _aiCostText = string.Empty;
    private string _alertWebhookUrl = string.Empty;
    private string _alertDeliveryStatus = "No webhook configured; alerts stay in-app.";
    private int _intervalMinutes;
    private bool _checking;

    public MonitoringViewModel(
        GovernanceWorkspace workspace,
        ISettingsStore settings,
        IAiUsageStore aiUsage,
        AlertNotifier notifier)
    {
        _workspace = workspace;
        _settings = settings;
        _aiUsage = aiUsage;
        _notifier = notifier;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CheckNowCommand = new AsyncRelayCommand(CheckNowAsync, () => !_checking);
        SaveScheduleCommand = new AsyncRelayCommand(SaveScheduleAsync);
        SendAlertsCommand = new AsyncRelayCommand(SendAlertsAsync);

        _timer = new DispatcherTimer();
        _timer.Tick += OnTimerTick;
    }

    public override string Title => "Monitoring";
    public override string Subtitle => "Configuration drift, licence utilisation and continuous governance signals.";

    public ObservableCollection<DriftItem> Drift { get; } = new();
    public ObservableCollection<LicenseInsight> LicenseInsights { get; } = new();
    public ObservableCollection<LicenseSkuRow> Licenses { get; } = new();
    public ObservableCollection<AlertItem> Alerts { get; } = new();

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ScheduleText { get => _scheduleText; private set => SetProperty(ref _scheduleText, value); }
    public string AlertWebhookUrl { get => _alertWebhookUrl; set => SetProperty(ref _alertWebhookUrl, value); }
    public string AlertDeliveryStatus { get => _alertDeliveryStatus; private set => SetProperty(ref _alertDeliveryStatus, value); }
    public string AiUsageText { get => _aiUsageText; private set => SetProperty(ref _aiUsageText, value); }
    public string AiCostText { get => _aiCostText; private set => SetProperty(ref _aiCostText, value); }
    public int IntervalMinutes { get => _intervalMinutes; set => SetProperty(ref _intervalMinutes, value); }

    public ICommand RefreshCommand { get; }
    public ICommand CheckNowCommand { get; }
    public ICommand SaveScheduleCommand { get; }
    public ICommand SendAlertsCommand { get; }

    public override async Task RefreshAsync()
    {
        Drift.Clear();
        LicenseInsights.Clear();
        Licenses.Clear();
        Alerts.Clear();

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

        var current = _workspace.Assessment;
        var previous = _workspace.History.Skip(1).FirstOrDefault();

        if (current is not null && _workspace.History.Count == 0)
        {
            await _workspace.RefreshHistoryAsync(current.TenantId).ConfigureAwait(true);
            previous = _workspace.History.Skip(1).FirstOrDefault();
        }

        foreach (var alert in AlertBuilder.Build(current, previous, _workspace.Drift, DateTimeOffset.Now))
        {
            Alerts.Add(alert);
        }

        var settings = await _settings.LoadAsync().ConfigureAwait(true);
        IntervalMinutes = settings.MonitoringIntervalMinutes;
        AlertWebhookUrl = settings.AlertWebhookUrl ?? string.Empty;
        ConfigureSchedule(settings.MonitoringIntervalMinutes);

        var summary = AiUsageSummary.From(await _aiUsage.GetTotalsAsync().ConfigureAwait(true));
        if (summary.Requests == 0)
        {
            AiUsageText = "No AI requests recorded yet.";
            AiCostText = "Provider keys resolve from environment variables and are never persisted.";
        }
        else
        {
            AiUsageText = $"{summary.Requests} request(s) · {summary.TotalTokens:N0} tokens · {summary.SuccessRatePercent:0.#}% success · {summary.FailedRequests} failed";
            AiCostText = $"Estimated spend ${summary.CostUsd:0.0000} across {summary.Providers} provider(s). Figures come from the local ledger, not a provider invoice.";
        }

        Status = Drift.Count == 0
            ? "No drift detected against the previous collection."
            : $"{Drift.Count} drift item(s) and {Alerts.Count} alert(s) detected since the last assessment.";
    }

    private void ConfigureSchedule(int intervalMinutes)
    {
        _timer.Stop();

        if (intervalMinutes <= 0)
        {
            ScheduleText = "Automatic checks are off. Use Check now, or set an interval in minutes.";
            return;
        }

        var minutes = Math.Max(15, intervalMinutes);
        _timer.Interval = TimeSpan.FromMinutes(minutes);
        _timer.Start();
        ScheduleText = $"Automatic checks run every {minutes} minutes while the app is open.";
    }

    private async Task SaveScheduleAsync()
    {
        try
        {
            var settings = await _settings.LoadAsync().ConfigureAwait(true);
            settings.MonitoringIntervalMinutes = Math.Max(0, IntervalMinutes);
            settings.AlertWebhookUrl = string.IsNullOrWhiteSpace(AlertWebhookUrl) ? null : AlertWebhookUrl.Trim();
            await _settings.SaveAsync(settings).ConfigureAwait(true);

            ConfigureSchedule(settings.MonitoringIntervalMinutes);
            Status = settings.MonitoringIntervalMinutes <= 0
                ? "Automatic checks disabled; the webhook setting was saved."
                : $"Automatic checks enabled every {Math.Max(15, settings.MonitoringIntervalMinutes)} minutes; the webhook setting was saved.";
        }
        catch (Exception ex)
        {
            Status = $"Schedule save failed: {ex.Message}";
        }
    }

    private async Task SendAlertsAsync()
    {
        var url = AlertWebhookUrl.Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            AlertDeliveryStatus = "Set an https webhook URL first.";
            return;
        }

        await DeliverAlertsAsync(url).ConfigureAwait(true);
    }

    private async Task DeliverAlertsAsync(string url)
    {
        try
        {
            var result = await _notifier.SendAsync(url, Alerts.ToList(), DateTimeOffset.Now).ConfigureAwait(true);
            AlertDeliveryStatus = result.Message;
        }
        catch (Exception ex)
        {
            AlertDeliveryStatus = $"Alert delivery failed: {ex.Message}";
        }
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        await CheckNowAsync().ConfigureAwait(true);
    }

    private async Task CheckNowAsync()
    {
        if (_checking) return;

        _checking = true;
        (CheckNowCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();

        try
        {
            var settings = await _settings.LoadAsync().ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(settings.Auth.TenantId) || string.IsNullOrWhiteSpace(settings.Auth.ClientId))
            {
                Status = "Check skipped: no tenant connection is configured. Use sample data on Assessment for a dry run.";
                return;
            }

            Status = "Running a scheduled check: Graph and Purview reads only…";
            await _workspace.AssessAsync(settings.Auth, settings.DefaultMode).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);

            Status = $"Check completed at {DateTimeOffset.Now:HH:mm}. {Alerts.Count} alert(s) active.";

            if (!string.IsNullOrWhiteSpace(settings.AlertWebhookUrl) && Alerts.Count > 0)
            {
                await DeliverAlertsAsync(settings.AlertWebhookUrl).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Status = $"Scheduled check failed: {ex.Message}";
        }
        finally
        {
            _checking = false;
            (CheckNowCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }
}

public sealed record LicenseSkuRow(string PartNumber, int Consumed, int Enabled, double Utilization)
{
    public string UtilizationText => $"{Utilization:0.#}%";
}
