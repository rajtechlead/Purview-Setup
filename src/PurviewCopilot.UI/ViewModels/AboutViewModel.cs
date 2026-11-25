using PurviewCopilot.UI.Mvvm;

namespace PurviewCopilot.UI.ViewModels;

public sealed class AboutViewModel : PageViewModel
{
    public override string Title => "About";
    public override string Subtitle => "Purview Governance Copilot — enterprise assessment and governance workspace.";

    public string Product => "Purview Governance Copilot";
    public string Version => "1.0.0";
    public string DesignedBy => "Raj Raja";
    public string Owner => "Belle-RajaCorp";
    public string Contact => "contact@belle-rajacorp.com";
    public string Platform => ".NET 9 · WPF · Microsoft Graph · Security & Compliance PowerShell · SQLite";
    public string SupportNote =>
        "Assessment and reporting are read-only. Tenant changes require a generated change plan, simulation and a completed approval workflow. AI prompts are sanitized locally and audited.";

    public string BuildInfo => $"Build {typeof(AboutViewModel).Assembly.GetName().Version} · {Environment.OSVersion}";
}
