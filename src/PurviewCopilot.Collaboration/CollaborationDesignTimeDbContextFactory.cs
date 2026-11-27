using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PurviewCopilot.Collaboration;

/// <summary>
/// Design-time context used only by <c>dotnet ef</c>. It points at a throwaway SQLite file and is
/// never used at runtime; the host configures the real provider.
/// </summary>
public sealed class CollaborationDesignTimeDbContextFactory : IDesignTimeDbContextFactory<CollaborationDbContext>
{
    public CollaborationDbContext CreateDbContext(string[] args) =>
        new(CollaborationStoreFactory.FromConfiguration("Sqlite", "Data Source=collaboration-design.db"));
}
