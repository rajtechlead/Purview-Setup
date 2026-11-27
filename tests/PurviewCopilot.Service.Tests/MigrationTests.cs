using Microsoft.EntityFrameworkCore;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Collaboration;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Service.Tests;

public class MigrationTests
{
    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class Factory : IDbContextFactory<CollaborationDbContext>
    {
        private readonly DbContextOptions<CollaborationDbContext> _options;

        public Factory(string path) =>
            _options = CollaborationStoreFactory.FromConfiguration("Sqlite", $"Data Source={path}");

        public CollaborationDbContext CreateDbContext() => new(_options);
    }

    [Fact]
    public async Task FreshDatabase_AppliesTheVersionedMigration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pgc-migrate-{Guid.NewGuid():N}.db");

        try
        {
            var factory = new Factory(path);
            await using var db = factory.CreateDbContext();

            Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
            Assert.NotEmpty(db.Database.GetMigrations());

            await db.Database.MigrateAsync();

            var applied = await db.Database.GetAppliedMigrationsAsync();
            Assert.Contains(applied, m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task StoreInitialize_AppliesMigrationsAndRecordsTheVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pgc-migrate-{Guid.NewGuid():N}.db");

        try
        {
            var factory = new Factory(path);
            var store = new CollaborationStore(factory, new Clock());

            await store.InitializeAsync();
            await store.InitializeAsync(); // idempotent

            await using var db = factory.CreateDbContext();
            var schema = await db.Schema.SingleAsync();
            Assert.Equal(1, schema.Version);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MigratedSchema_ServesTheStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pgc-migrate-{Guid.NewGuid():N}.db");

        try
        {
            var store = new CollaborationStore(new Factory(path), new Clock());
            await store.InitializeAsync();

            await store.GrantMemberAsync("contoso.onmicrosoft.com", "analyst", CollaborationRole.Analyst, "test");

            Assert.Equal(
                CollaborationRole.Analyst,
                await store.GetRoleAsync("contoso.onmicrosoft.com", "analyst"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
