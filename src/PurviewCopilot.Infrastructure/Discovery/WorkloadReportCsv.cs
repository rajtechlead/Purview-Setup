namespace PurviewCopilot.Infrastructure.Discovery;

/// <summary>Counts data rows in a Graph usage-report CSV, which starts with one header row.</summary>
public static class WorkloadReportCsv
{
    public static int CountRows(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return 0;

        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Count(line => !string.IsNullOrWhiteSpace(line));

        return Math.Max(0, lines - 1);
    }
}
