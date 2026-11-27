using Microsoft.EntityFrameworkCore;

namespace PurviewCopilot.Collaboration;

public sealed class CollaborationDbContext : DbContext
{
    public CollaborationDbContext(DbContextOptions<CollaborationDbContext> options) : base(options) { }

    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();
    public DbSet<SharedAssessment> Assessments => Set<SharedAssessment>();
    public DbSet<SharedPlan> Plans => Set<SharedPlan>();
    public DbSet<SharedApproval> Approvals => Set<SharedApproval>();
    public DbSet<SharedAuditEntry> Audit => Set<SharedAuditEntry>();
    public DbSet<CollaborationSchema> Schema => Set<CollaborationSchema>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite cannot translate ORDER BY on DateTimeOffset; UTC DateTime is portable and sortable.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<NullableUtcDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantMembership>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.UserId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Role).HasMaxLength(64).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(256);
            entity.HasIndex(e => new { e.TenantId, e.UserId }).IsUnique();
            entity.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
        });

        modelBuilder.Entity<SharedAssessment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(256);
            entity.Property(e => e.PayloadJson).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.RunUtc });
            entity.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
        });

        modelBuilder.Entity<SharedPlan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(512);
            entity.Property(e => e.Mode).HasMaxLength(64);
            entity.Property(e => e.State).HasMaxLength(64);
            entity.Property(e => e.PlanHash).HasMaxLength(64);
            entity.Property(e => e.CreatedBy).HasMaxLength(256);
            entity.Property(e => e.PayloadJson).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.CreatedUtc });
            entity.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
        });

        modelBuilder.Entity<SharedApproval>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PlanId).IsUnique();
            entity.Property(e => e.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.State).HasMaxLength(64);
            entity.Property(e => e.PlanHash).HasMaxLength(64);
            entity.Property(e => e.PayloadJson).IsRequired();
            entity.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
        });

        modelBuilder.Entity<SharedAuditEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Actor).HasMaxLength(256);
            entity.Property(e => e.Action).HasMaxLength(128);
            entity.Property(e => e.Subject).HasMaxLength(256);
            entity.Property(e => e.PrevHash).HasMaxLength(64);
            entity.Property(e => e.Hash).HasMaxLength(64);
            entity.HasIndex(e => new { e.TenantId, e.Sequence }).IsUnique();
        });

        modelBuilder.Entity<CollaborationSchema>().HasKey(e => e.Id);
    }

    public override int SaveChanges()
    {
        Stamp();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Stamp();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void Stamp()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case TenantMembership membership when entry.State == EntityState.Modified:
                    membership.ConcurrencyStamp = Guid.NewGuid();
                    break;
                case TenantMembership membership when membership.ConcurrencyStamp == Guid.Empty:
                    membership.ConcurrencyStamp = Guid.NewGuid();
                    break;
                case SharedAssessment assessment when assessment.ConcurrencyStamp == Guid.Empty:
                    assessment.ConcurrencyStamp = Guid.NewGuid();
                    break;
                case SharedPlan plan when plan.ConcurrencyStamp == Guid.Empty:
                    plan.ConcurrencyStamp = Guid.NewGuid();
                    break;
                case SharedApproval approval when entry.State == EntityState.Modified:
                    approval.ConcurrencyStamp = Guid.NewGuid();
                    break;
                case SharedApproval approval when approval.ConcurrencyStamp == Guid.Empty:
                    approval.ConcurrencyStamp = Guid.NewGuid();
                    break;
            }
        }
    }
}

public static class CollaborationStoreFactory
{
    /// <summary>Provider names accepted from configuration. Anything else fails fast.</summary>
    public static DbContextOptions<CollaborationDbContext> FromConfiguration(string provider, string connectionString)
    {
        var builder = new DbContextOptionsBuilder<CollaborationDbContext>();
        Configure(builder, provider, connectionString);
        return builder.Options;
    }

    public static void Configure(DbContextOptionsBuilder builder, string provider, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A collaboration database connection string is required.", nameof(connectionString));
        }

        switch (provider?.Trim().ToLowerInvariant())
        {
            case "sqlite":
                builder.UseSqlite(connectionString);
                break;
            case "sqlserver":
                builder.UseSqlServer(connectionString);
                break;
            default:
                throw new ArgumentException(
                    $"Unsupported collaboration provider '{provider}'. Use Sqlite for a single-node install or SqlServer for a shared central database.",
                    nameof(provider));
        }
    }
}
