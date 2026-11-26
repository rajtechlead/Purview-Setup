using PurviewCopilot.Infrastructure.Discovery;
using Xunit;

namespace PurviewCopilot.Tests;

public class WorkloadReportCsvTests
{
    [Fact]
    public void CountsDataRowsAndIgnoresTheHeader()
    {
        const string csv = "Report Date,User Principal Name,Display Name,Is Deleted,Item Count\r\n2026-11-20,a@contoso.com,A,False,10\r\n2026-11-20,b@contoso.com,B,False,4\r\n";

        Assert.Equal(2, WorkloadReportCsv.CountRows(csv));
    }

    [Fact]
    public void HandlesEmptyAndHeaderOnlyReports()
    {
        Assert.Equal(0, WorkloadReportCsv.CountRows(null));
        Assert.Equal(0, WorkloadReportCsv.CountRows(string.Empty));
        Assert.Equal(0, WorkloadReportCsv.CountRows("Report Date,User Principal Name\r\n"));
    }
}
