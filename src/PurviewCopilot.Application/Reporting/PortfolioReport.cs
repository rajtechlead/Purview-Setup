using System.Globalization;
using System.Text;
using PurviewCopilot.Application.Workspace;

namespace PurviewCopilot.Application.Reporting;

/// <summary>Cross-tenant portfolio export. One row per customer; nothing below the aggregate crosses tenants.</summary>
public static class PortfolioReport
{
    public const string Header = "Customer,TenantId,Assessed,Overall,Security,Compliance,Risk,Maturity,LastAssessedUtc";

    public static string BuildCsv(IReadOnlyList<PortfolioRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Header);

        foreach (var row in rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            builder
                .Append(Escape(row.DisplayName)).Append(',')
                .Append(Escape(row.TenantId)).Append(',')
                .Append(row.Assessed ? "Yes" : "No").Append(',')
                .Append(row.Assessed ? Number(row.Overall) : string.Empty).Append(',')
                .Append(row.Assessed ? Number(row.Security) : string.Empty).Append(',')
                .Append(row.Assessed ? Number(row.Compliance) : string.Empty).Append(',')
                .Append(row.Assessed ? Number(row.Risk) : string.Empty).Append(',')
                .Append(Escape(row.MaturityText)).Append(',')
                .Append(row.AssessedUtc is { } at ? at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : string.Empty)
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
