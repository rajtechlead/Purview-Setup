using PurviewCopilot.Application.Reporting;
using PurviewCopilot.Application.Workspace;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class PortfolioReportTests
{
    [Fact]
    public void Csv_HasHeaderAndOneRowPerTenant()
    {
        var rows = new[]
        {
            new PortfolioRow("tenant-a", "Contoso", true, 81.5, 79, 84, 22, MaturityLevel.Managed,
                new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero)),
            new PortfolioRow("tenant-b", "Fabrikam", false, 0, 0, 0, 0, MaturityLevel.Initial, null)
        };

        var csv = PortfolioReport.BuildCsv(rows);
        var lines = csv.TrimEnd().Split(Environment.NewLine);

        Assert.Equal(PortfolioReport.Header, lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("Contoso,tenant-a,Yes,81.5,79,84,22,Managed,2026-11-25 10:00", lines[1], StringComparison.Ordinal);
        Assert.Equal("Fabrikam,tenant-b,No,,,,,Not assessed,", lines[2]);
    }

    [Fact]
    public void Csv_EscapesCommasAndQuotes()
    {
        var rows = new[]
        {
            new PortfolioRow("tenant-a", "Contoso, \"West\"", false, 0, 0, 0, 0, MaturityLevel.Initial, null)
        };

        var csv = PortfolioReport.BuildCsv(rows);

        Assert.Contains("\"Contoso, \"\"West\"\"\"", csv, StringComparison.Ordinal);
    }
}
