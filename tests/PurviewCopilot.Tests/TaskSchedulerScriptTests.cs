using PurviewCopilot.Application.Monitoring;
using Xunit;

namespace PurviewCopilot.Tests;

public class TaskSchedulerScriptTests
{
    private static TaskScheduleOptions Options() => new(
        @"C:\Tools\pgc.exe",
        "11111111-1111-1111-1111-111111111111",
        "22222222-2222-2222-2222-222222222222",
        "ABCDEF123456",
        @"C:\ProgramData\PurviewCopilot",
        @"C:\Reports\Purview",
        "06:30",
        "Purview daily assessment");

    [Fact]
    public void Script_RegistersDailyTaskAndRunsTheCli()
    {
        var script = TaskSchedulerScript.Build(Options());

        Assert.Contains("param([switch]$Register)", script, StringComparison.Ordinal);
        Assert.Contains("New-ScheduledTaskTrigger -Daily -At '06:30'", script, StringComparison.Ordinal);
        Assert.Contains("Register-ScheduledTask -TaskName 'Purview daily assessment'", script, StringComparison.Ordinal);
        Assert.Contains("& $pgc assess", script, StringComparison.Ordinal);
        Assert.Contains("--tenant '11111111-1111-1111-1111-111111111111'", script, StringComparison.Ordinal);
        Assert.Contains("--thumbprint 'ABCDEF123456'", script, StringComparison.Ordinal);
        Assert.Contains("--format html", script, StringComparison.Ordinal);
        Assert.Contains("$stamp = Get-Date -Format 'yyyyMMdd-HHmm'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_EscapesSingleQuotesInPaths()
    {
        var script = TaskSchedulerScript.Build(Options() with
        {
            CliPath = @"C:\O'Brien\pgc.exe",
            TaskName = "Owner's assessment"
        });

        Assert.Contains(@"C:\O''Brien\pgc.exe", script, StringComparison.Ordinal);
        Assert.Contains("Owner''s assessment", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("6am")]
    [InlineData("24:00")]
    public void InvalidRunTime_IsRejected(string runAt)
    {
        Assert.Throws<ArgumentException>(() => TaskSchedulerScript.Build(Options() with { RunAt = runAt }));
    }

    [Fact]
    public void MissingCertificate_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => TaskSchedulerScript.Build(Options() with { Thumbprint = " " }));
    }
}
