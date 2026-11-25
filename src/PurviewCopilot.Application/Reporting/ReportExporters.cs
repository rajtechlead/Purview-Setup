using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Reporting;

/// <summary>Self-contained HTML report: no external CSS, scripts or fonts, safe for offline and air-gapped use.</summary>
public sealed class HtmlReportExporter : IReportExporter
{
    public ReportFormat Format => ReportFormat.Html;
    public string FileExtension => ".html";

    public string Render(ReportContext context)
    {
        var a = context.Assessment;
        var s = a.Scores;
        var sb = new StringBuilder();

        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append($"<title>Purview Governance Report — {E(context.OrganizationName)}</title>");
        sb.Append("<style>");
        sb.Append(":root{--ink:#16213b;--muted:#5b6b85;--accent:#1f6feb}");
        sb.Append("*{box-sizing:border-box}body{margin:0;background:#f4f7fb;color:#16213b;font:14px/1.5 'Segoe UI',system-ui,sans-serif}");
        sb.Append("header{background:linear-gradient(120deg,#0b2447,#19376d);color:#fff;padding:32px 40px}");
        sb.Append("header h1{margin:0 0 4px;font-size:26px}header p{margin:0;color:#cfe0ff}");
        sb.Append("main{padding:28px 40px;max-width:1180px;margin:0 auto}");
        sb.Append(".kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:12px;margin:18px 0}");
        sb.Append(".kpi{background:#fff;border:1px solid #dbe4f0;border-radius:10px;padding:14px}");
        sb.Append(".kpi span{display:block;color:#5b6b85;font-size:12px;text-transform:uppercase;letter-spacing:.06em}");
        sb.Append(".kpi strong{font-size:26px}");
        sb.Append("h2{font-size:18px;margin:28px 0 10px;border-bottom:1px solid #dbe4f0;padding-bottom:6px}");
        sb.Append("table{width:100%;border-collapse:collapse;background:#fff;font-size:13px}");
        sb.Append("th,td{border:1px solid #e2e9f3;padding:7px 9px;text-align:left;vertical-align:top}");
        sb.Append("th{background:#eef4fd;font-weight:600}");
        sb.Append(".PASS{color:#1e8449;font-weight:600}.WARNING{color:#b9770e;font-weight:600}.FAIL{color:#c0392b;font-weight:600}");
        sb.Append("footer{margin:36px 0;color:#5b6b85;font-size:12px;text-align:center}");
        sb.Append("</style></head><body>");

        sb.Append("<header><h1>Purview Governance Copilot</h1>");
        sb.Append($"<p>{E(context.OrganizationName)} · Generated {context.GeneratedUtc:yyyy-MM-dd HH:mm} UTC · by {E(context.GeneratedBy)}</p></header><main>");

        sb.Append("<div class=\"kpis\">");
        Kpi(sb, "Overall", s.Overall);
        Kpi(sb, "Security", s.Security);
        Kpi(sb, "Governance", s.Governance);
        Kpi(sb, "Compliance", s.Compliance);
        Kpi(sb, "Maturity", s.Overall);
        Kpi(sb, "Risk exposure", s.Risk);
        Kpi(sb, "AI governance", s.AiGovernance);
        Kpi(sb, "Copilot readiness", s.CopilotReadiness);
        sb.Append("</div>");

        sb.Append($"<p><strong>Maturity band:</strong> {E(s.MaturityLevel.ToString())} · <strong>Findings:</strong> {a.Findings.Count} · ");
        sb.Append($"<strong>Recommendations:</strong> {a.Recommendations.Count} · <strong>Rules evaluated:</strong> {a.RuleCount}</p>");

        sb.Append("<h2>Module scores</h2><table><tr><th>Module</th><th>Score</th><th>Assessed</th><th>Pass</th><th>Warning</th><th>Fail</th></tr>");
        foreach (var m in s.Modules.OrderByDescending(m => m.Assessed))
        {
            sb.Append($"<tr><td>{E(m.Module.ToString())}</td><td>{m.Score:0.#}</td><td>{m.Assessed}</td><td>{m.Passed}</td><td>{m.Warnings}</td><td>{m.Failed}</td></tr>");
        }
        sb.Append("</table>");

        sb.Append("<h2>Framework coverage</h2><table><tr><th>Framework</th><th>Coverage</th><th>Controls</th><th>Gaps</th></tr>");
        foreach (var f in context.Frameworks)
        {
            sb.Append($"<tr><td>{E(f.Framework)}</td><td>{f.Percent:0.#}%</td><td>{f.CoveredControls}/{f.TotalControls}</td><td>{E(string.Join("; ", f.Gaps.Take(6)))}</td></tr>");
        }
        sb.Append("</table>");

        sb.Append("<h2>Findings</h2><table><tr><th>ID</th><th>Module</th><th>Finding</th><th>Status</th><th>Severity</th><th>Evidence</th><th>Remediation</th></tr>");
        foreach (var f in a.Findings.OrderByDescending(f => f.Severity).ThenBy(f => f.Id))
        {
            sb.Append($"<tr><td>{E(f.Id)}</td><td>{E(f.Module.ToString())}</td><td>{E(f.Title)}</td>");
            sb.Append($"<td class=\"{f.Status.ToString().ToUpperInvariant()}\">{f.Status}</td><td>{f.Severity}</td>");
            sb.Append($"<td>{E(f.Evidence)}</td><td>{E(f.Remediation)}</td></tr>");
        }
        sb.Append("</table>");

        sb.Append("<h2>Recommendations</h2><table><tr><th>ID</th><th>Priority</th><th>Recommendation</th><th>Licences</th><th>Change risk</th></tr>");
        foreach (var r in a.Recommendations.Take(60))
        {
            sb.Append($"<tr><td>{E(r.Id)}</td><td>{r.Priority}</td><td>{E(r.Title)}</td><td>{E(string.Join(", ", r.RequiredLicenses))}</td><td>{E(r.ChangeRisk)}</td></tr>");
        }
        sb.Append("</table>");

        sb.Append($"<footer>Generated by Purview Governance Copilot · Belle-RajaCorp · Assessment {a.Id}</footer>");
        sb.Append("</main></body></html>");

        return sb.ToString();
    }

    private static void Kpi(StringBuilder sb, string label, double value) =>
        sb.Append($"<div class=\"kpi\"><span>{E(label)}</span><strong>{value:0.#}</strong></div>");

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

public sealed class JsonReportExporter : IReportExporter
{
    public ReportFormat Format => ReportFormat.Json;
    public string FileExtension => ".json";

    public string Render(ReportContext context) =>
        JsonSerializer.Serialize(new
        {
            organization = context.OrganizationName,
            generatedUtc = context.GeneratedUtc,
            generatedBy = context.GeneratedBy,
            tenant = new { context.Tenant.TenantId, context.Tenant.TenantName, context.Tenant.CollectedUtc },
            assessment = context.Assessment,
            frameworks = context.Frameworks
        }, new JsonSerializerOptions { WriteIndented = true });
}

public sealed class CsvReportExporter : IReportExporter
{
    public ReportFormat Format => ReportFormat.Csv;
    public string FileExtension => ".csv";

    public string Render(ReportContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,Module,Title,Status,Severity,Evidence,Remediation,Frameworks");

        foreach (var f in context.Assessment.Findings.OrderBy(f => f.Module))
        {
            sb.AppendLine(string.Join(",",
                Q(f.Id), Q(f.Module.ToString()), Q(f.Title), Q(f.Status.ToString()), Q(f.Severity.ToString()),
                Q(f.Evidence), Q(f.Remediation), Q(string.Join("; ", f.FrameworkRefs))));
        }

        return sb.ToString();
    }

    private static string Q(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}

public sealed class MarkdownReportExporter : IReportExporter
{
    public ReportFormat Format => ReportFormat.Markdown;
    public string FileExtension => ".md";

    public string Render(ReportContext context)
    {
        var a = context.Assessment;
        var sb = new StringBuilder();

        sb.AppendLine($"# Purview Governance Report — {context.OrganizationName}");
        sb.AppendLine();
        sb.AppendLine($"_Generated {context.GeneratedUtc:yyyy-MM-dd HH:mm} UTC by {context.GeneratedBy}_");
        sb.AppendLine();
        sb.AppendLine("## Scores");
        sb.AppendLine();
        sb.AppendLine("| Metric | Score |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| Overall | {a.Scores.Overall:0.#} |");
        sb.AppendLine($"| Security | {a.Scores.Security:0.#} |");
        sb.AppendLine($"| Governance | {a.Scores.Governance:0.#} |");
        sb.AppendLine($"| Compliance | {a.Scores.Compliance:0.#} |");
        sb.AppendLine($"| Risk exposure | {a.Scores.Risk:0.#} |");
        sb.AppendLine($"| AI governance | {a.Scores.AiGovernance:0.#} |");
        sb.AppendLine($"| Copilot readiness | {a.Scores.CopilotReadiness:0.#} |");
        sb.AppendLine($"| Maturity | {a.Scores.MaturityLevel} |");
        sb.AppendLine();
        sb.AppendLine("## Framework coverage");
        sb.AppendLine();
        sb.AppendLine("| Framework | Coverage | Gaps |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var f in context.Frameworks)
        {
            sb.AppendLine($"| {f.Framework} | {f.Percent:0.#}% | {string.Join("; ", f.Gaps.Take(4))} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Findings");
        sb.AppendLine();
        sb.AppendLine("| ID | Module | Finding | Status | Severity |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var f in a.Findings.OrderByDescending(f => f.Severity))
        {
            sb.AppendLine($"| {f.Id} | {f.Module} | {f.Title} | {f.Status} | {f.Severity} |");
        }

        return sb.ToString();
    }
}
