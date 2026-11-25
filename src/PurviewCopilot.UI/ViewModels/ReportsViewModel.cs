using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Workspace;
using PurviewCopilot.Domain;
using PurviewCopilot.UI.Mvvm;

namespace PurviewCopilot.UI.ViewModels;

public sealed class ReportsViewModel : PageViewModel
{
    private readonly GovernanceWorkspace _workspace;
    private string _status = "Run an assessment to generate reports.";
    private string _organization = string.Empty;

    public ReportsViewModel(GovernanceWorkspace workspace)
    {
        _workspace = workspace;
        ExportHtmlCommand = new AsyncRelayCommand(() => ExportAsync(ReportFormat.Html, ".html"));
        ExportJsonCommand = new AsyncRelayCommand(() => ExportAsync(ReportFormat.Json, ".json"));
        ExportCsvCommand = new AsyncRelayCommand(() => ExportAsync(ReportFormat.Csv, ".csv"));
        ExportMarkdownCommand = new AsyncRelayCommand(() => ExportAsync(ReportFormat.Markdown, ".md"));
        ExportPdfCommand = new AsyncRelayCommand(() => ExportAsync(ReportFormat.Pdf, ".pdf"));
        ExportDocxCommand = new AsyncRelayCommand(() => ExportAsync(ReportFormat.Docx, ".docx"));
        ExportXlsxCommand = new AsyncRelayCommand(() => ExportAsync(ReportFormat.Xlsx, ".xlsx"));
    }

    public override string Title => "Reports";
    public override string Subtitle => "Executive readiness against ISO 27001, NIST CSF, CIS, SOC 2, HIPAA, GDPR and PCI-DSS.";

    public ObservableCollection<FrameworkCoverage> Frameworks { get; } = new();

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public string Organization
    {
        get => _organization;
        set => SetProperty(ref _organization, value);
    }

    public ICommand ExportHtmlCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportMarkdownCommand { get; }
    public ICommand ExportPdfCommand { get; }
    public ICommand ExportDocxCommand { get; }
    public ICommand ExportXlsxCommand { get; }

    public override Task RefreshAsync()
    {
        Frameworks.Clear();
        foreach (var framework in _workspace.Frameworks)
        {
            Frameworks.Add(framework);
        }

        if (string.IsNullOrWhiteSpace(Organization))
        {
            Organization = _workspace.Tenant?.TenantName ?? "Organization";
        }

        Status = _workspace.Assessment is null
            ? "Run an assessment to generate reports."
            : $"{Frameworks.Count} framework(s) mapped from {_workspace.Assessment.Findings.Count} findings.";

        return Task.CompletedTask;
    }

    private async Task ExportAsync(ReportFormat format, string extension)
    {
        if (_workspace.Assessment is null)
        {
            Status = "Run an assessment before exporting.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = $"Report (*{extension})|*{extension}",
            FileName = $"purview-governance-{DateTime.Now:yyyyMMdd-HHmm}{extension}",
            Title = "Export governance report"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            await _workspace.ExportReportAsync(format, dialog.FileName, Organization, Environment.UserName).ConfigureAwait(true);
            Status = $"Exported {format} report to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            Status = $"Export failed: {ex.Message}";
        }
    }
}
