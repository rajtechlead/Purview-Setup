using System.Collections.ObjectModel;
using System.Windows.Input;
using PurviewCopilot.UI.Mvvm;

namespace PurviewCopilot.UI.ViewModels;

public sealed record HelpTopic(string Title, string Category, string Body);

public sealed class HelpViewModel : PageViewModel
{
    private string _search = string.Empty;
    private HelpTopic? _selected;

    public HelpViewModel()
    {
        _selected = Topics.FirstOrDefault();
        ApplyFilterCommand = new RelayCommand(ApplyFilter);
    }

    public override string Title => "Help Center";
    public override string Subtitle => "Plain-language explanations of every capability this tool assesses.";

    public ObservableCollection<HelpTopic> Filtered { get; } = new();

    public string Search
    {
        get => _search;
        set { if (SetProperty(ref _search, value)) ApplyFilter(); }
    }

    public HelpTopic? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public ICommand ApplyFilterCommand { get; }

    public static IReadOnlyList<HelpTopic> Topics { get; } = new[]
    {
        new HelpTopic("What is Microsoft Purview?", "Foundations",
            "Microsoft Purview is Microsoft's data security, compliance and governance platform. It brings together sensitivity labels, data loss prevention, retention, records management, audit, insider risk, eDiscovery and data security posture management (DSPM) in one portal at purview.microsoft.com. It also includes a data map for cataloguing data across clouds and on-premises sources. This tool assesses the security and compliance half of Purview by reading configuration through Microsoft Graph and Security & Compliance PowerShell; it never changes anything unless you build, simulate and approve a change plan."),
        new HelpTopic("Sensitivity labels", "Information protection",
            "A sensitivity label is a tag applied to a file, email, site or meeting that describes how sensitive it is and can enforce protection. Labels can encrypt content, add watermarks or headers, restrict access and travel with the item outside the tenant. Start with four levels (Public, General, Confidential, Highly Confidential), publish them through a label policy to a pilot group, then enable default and mandatory labelling. Labels are the foundation for DLP, Copilot governance and insider risk because every other control needs to know what data matters."),
        new HelpTopic("Data Loss Prevention (DLP)", "Data protection",
            "DLP finds sensitive information in email, SharePoint, OneDrive, Teams, endpoints and browser uploads, then warns, blocks or audits the action. A policy combines locations, sensitive information types, conditions and actions. Always run in simulation first: DLP that blocks legitimate work is worse than no DLP, because users learn to bypass it. The safe pattern is policy in test mode, daily match review for ten business days, tune thresholds, then enforce with an override path and a documented approval."),
        new HelpTopic("Endpoint DLP", "Data protection",
            "Endpoint DLP extends protection to activity on managed Windows and macOS devices: copying to USB, printing, uploading to personal cloud storage and using unapproved apps. Devices must be onboarded first, normally through Intune or a Defender onboarding package. Coverage matters more than policy complexity: track onboarded devices against the in-scope population and start with an audit-only pilot of about ten devices before enforcing."),
        new HelpTopic("DSPM and DSPM for AI", "Posture",
            "Data Security Posture Management continuously evaluates where sensitive data lives, who can reach it and which risks are unmanaged. DSPM for AI adds visibility into prompts and responses sent to generative AI applications, including Microsoft 365 Copilot and third-party AI sites. Enable DSPM before Copilot so oversharing is visible, then connect its recommendations to DLP policies and label changes with named owners."),
        new HelpTopic("eDiscovery", "Compliance",
            "eDiscovery is the process of finding, preserving, reviewing and exporting content for legal or investigation purposes. Standard eDiscovery covers cases, searches, holds and export. Advanced eDiscovery adds custodian management, review sets, analytics and a legal workflow. The process matters as much as the licence: test a complete case (search, hold, review, export) before a real legal request arrives, and document who can create and manage cases."),
        new HelpTopic("Insider Risk Management", "Insider risk",
            "Insider Risk Management correlates signals such as downloads, exfiltration to personal storage, departing-user activity and security policy violations into risk insights. It is a high-trust capability: engage HR and legal before enabling indicators, decide deliberately whether alerts are anonymized, and publish a triage process with an SLA. Without that governance, insider risk tools create privacy exposure and erode trust."),
        new HelpTopic("AI governance", "AI",
            "AI governance means controlling how generative AI is used with company data. In Purview terms: an acceptable-use policy, DSPM for AI visibility, DLP rules that catch sensitive prompts and responses, audit retention for AI interactions, and an inventory of approved AI services and owners. Map this to ISO 42001 and the NIST AI RMF. The goal is not to block AI; it is to make its data flows visible, bounded and auditable."),
        new HelpTopic("Microsoft 365 Copilot governance", "AI",
            "Copilot can only surface content a user can already access, so oversharing is the primary risk. Before rollout: publish a graduated label set, remove broad SharePoint sharing links, enable audit so Copilot interactions can be investigated, restrict agent creation, and review DSPM for AI findings weekly. Licence prerequisites matter too: Copilot requires a qualifying base licence and enough data protection to be safe."),
        new HelpTopic("Compliance Manager", "Compliance",
            "Compliance Manager scores your tenant against regulatory templates such as ISO 27001, NIST CSF, SOC 2, HIPAA, GDPR and PCI-DSS, and turns gaps into improvement actions. Use it as a prioritisation input, not a grade: a high score with unlabelled data is not compliance. This tool maps its own findings to the same frameworks so executive reporting stays consistent with the improvement actions your teams already own."),
        new HelpTopic("How scoring works", "Using this tool",
            "Each control produces one finding: Pass, Warning or Fail, based on collected evidence. A module's score is the average of its assessed controls (Pass 100, Warning 60, Fail 0). Domain scores are weighted averages across relevant modules, and the risk score is a severity-weighted count of open findings where lower is better. Scores are deterministic and reproducible, which is what makes them defensible in an executive report."),
        new HelpTopic("Privacy guard for AI", "Using this tool",
            "Before any AI call, the prompt and the assessment context are scanned locally for email addresses, phone numbers, national IDs, payment cards, IBANs, secrets, credentials, tenant identifiers and SharePoint URLs. Depending on the configured mode, matches are redacted, masked, tokenized or blocked outright. Nothing is sent until you review the sanitized prompt, and every decision is written to the local audit log. In offline mode only local providers (Ollama, LM Studio, Open WebUI) are used.")
    };

    public void ApplyFilter()
    {
        Filtered.Clear();

        var query = string.IsNullOrWhiteSpace(Search)
            ? Topics
            : Topics.Where(t =>
                t.Title.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                t.Category.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                t.Body.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var topic in query)
        {
            Filtered.Add(topic);
        }

        if (Selected is null || !Filtered.Contains(Selected))
        {
            Selected = Filtered.FirstOrDefault();
        }
    }

    public override Task RefreshAsync()
    {
        ApplyFilter();
        return Task.CompletedTask;
    }
}

