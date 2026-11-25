using Microsoft.Data.Sqlite;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;
using PurviewCopilot.Infrastructure.Data;
using Xunit;

namespace PurviewCopilot.Tests;

public class FindingActionTests
{
    [Fact]
    public async Task Actions_RoundTripPerTenant_AndEncryptNotes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pgc-actions-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var store = new SqliteStore(directory);
            var actions = (IFindingActionStore)store;
            var due = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
            var at = new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

            await actions.SaveAsync(new FindingAction(
                "DLP-01", "tenant-a", "alice", due, ActionStatus.InProgress, "Contoso-Secret-Action-Note", at));

            await actions.SaveAsync(new FindingAction(
                "DLP-01", "tenant-b", "bob", null, ActionStatus.Blocked, "other tenant", at));

            var tenantA = await actions.ListAsync("tenant-a");
            var tenantB = await actions.ListAsync("tenant-b");

            var action = Assert.Single(tenantA);
            Assert.Equal("DLP-01", action.FindingId);
            Assert.Equal("tenant-a", action.TenantId);
            Assert.Equal("alice", action.Owner);
            Assert.Equal(due, action.DueUtc);
            Assert.Equal(ActionStatus.InProgress, action.Status);
            Assert.Equal("Contoso-Secret-Action-Note", action.Notes);

            Assert.Equal("bob", Assert.Single(tenantB).Owner);

            using var connection = new SqliteConnection($"Data Source={store.DatabasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT notes FROM finding_actions WHERE tenant_id='tenant-a'";
            var raw = (string)command.ExecuteScalar()!;

            Assert.DoesNotContain("Contoso-Secret-Action-Note", raw, StringComparison.Ordinal);
            Assert.StartsWith("dpapi:v1:", raw, StringComparison.Ordinal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task SavingTheSameFindingTwice_UpdatesInsteadOfDuplicating()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pgc-actions-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var store = new SqliteStore(directory);
            var actions = (IFindingActionStore)store;
            var at = new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

            await actions.SaveAsync(new FindingAction("AUD-01", "tenant-a", "", null, ActionStatus.Open, "", at));
            await actions.SaveAsync(new FindingAction("AUD-01", "tenant-a", "carol", null, ActionStatus.Done, "closed", at.AddHours(1)));

            var action = Assert.Single(await actions.ListAsync("tenant-a"));
            Assert.Equal("carol", action.Owner);
            Assert.Equal(ActionStatus.Done, action.Status);
            Assert.Equal("closed", action.Notes);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
