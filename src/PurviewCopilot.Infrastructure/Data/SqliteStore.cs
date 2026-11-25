using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;
using PurviewCopilot.Infrastructure.Security;

namespace PurviewCopilot.Infrastructure.Data;

/// <summary>
/// Single SQLite store implementing every persistence port. Local-only by design: no telemetry,
/// no cloud dependency. Optional SQL Server can be added by implementing the same ports.
/// </summary>
public sealed class SqliteStore :
    IAssessmentStore,
    IChangePlanStore,
    IApprovalStore,
    IBackupStore,
    IAuditLog,
    IAiUsageStore,
    ISettingsStore,
    ITenantProfileStore,
    IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;
    private readonly object _auditGate = new();

    public SqliteStore(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            throw new ArgumentException("A data directory is required.", nameof(dataDirectory));

        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "purviewcopilot.db");

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        Initialize();
    }

    public string DatabasePath => new SqliteConnectionStringBuilder(_connectionString).DataSource;

    /// <summary>Releases pooled file handles so the database file can be moved or deleted.</summary>
    public void Dispose() => SqliteConnection.ClearAllPools();

    private void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS assessments (
                id TEXT PRIMARY KEY,
                tenant_id TEXT NOT NULL,
                run_utc TEXT NOT NULL,
                mode TEXT NOT NULL,
                overall REAL NOT NULL,
                payload TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_assessments_tenant ON assessments(tenant_id, run_utc DESC);

            CREATE TABLE IF NOT EXISTS change_plans (
                id TEXT PRIMARY KEY,
                tenant_id TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                state TEXT NOT NULL,
                payload TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_plans_tenant ON change_plans(tenant_id, created_utc DESC);

            CREATE TABLE IF NOT EXISTS approvals (
                id TEXT PRIMARY KEY,
                plan_id TEXT NOT NULL UNIQUE,
                state TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                payload TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS backups (
                id TEXT PRIMARY KEY,
                tenant_id TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                label TEXT NOT NULL,
                payload TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_backups_tenant ON backups(tenant_id, created_utc DESC);

            CREATE TABLE IF NOT EXISTS audit_log (
                id TEXT PRIMARY KEY,
                at_utc TEXT NOT NULL,
                actor TEXT NOT NULL,
                action TEXT NOT NULL,
                target TEXT NOT NULL,
                detail TEXT NOT NULL,
                correlation_id TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_audit_at ON audit_log(at_utc DESC);

            CREATE TABLE IF NOT EXISTS ai_usage (
                id TEXT PRIMARY KEY,
                provider_id TEXT NOT NULL,
                model TEXT NOT NULL,
                prompt_tokens INTEGER NOT NULL,
                completion_tokens INTEGER NOT NULL,
                cost REAL NOT NULL,
                succeeded INTEGER NOT NULL,
                error_code TEXT NULL,
                at_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_ai_usage_at ON ai_usage(at_utc DESC);

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS tenant_profiles (
                tenant_id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                payload TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS baselines (
                tenant_id TEXT PRIMARY KEY,
                updated_utc TEXT NOT NULL,
                payload TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        EnsureAuditChain();
    }

    private void EnsureAuditChain()
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(audit_log)";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (!columns.Contains("prev_hash"))
        {
            Execute("ALTER TABLE audit_log ADD COLUMN prev_hash TEXT NULL");
        }

        if (!columns.Contains("entry_hash"))
        {
            Execute("ALTER TABLE audit_log ADD COLUMN entry_hash TEXT NULL");
        }

        if (int.TryParse(Scalar("SELECT COUNT(*) FROM audit_log WHERE entry_hash IS NULL"), out var missing) && missing > 0)
        {
            BackfillAuditChain();
        }
    }

    /// <summary>
    /// Databases created before the chain existed are hashed in insertion order so the chain is
    /// valid from the upgrade point forward. It cannot attest to edits made before that point.
    /// </summary>
    private void BackfillAuditChain()
    {
        var rows = new List<(long RowId, string Id, string At, string Actor, string Action, string Target, string Detail, string? Correlation)>();

        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT rowid, id, at_utc, actor, action, target, detail, correlation_id FROM audit_log ORDER BY rowid ASC";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }

        var previous = string.Empty;
        using var writeConnection = Open();
        using var transaction = writeConnection.BeginTransaction();

        foreach (var row in rows)
        {
            var hash = AuditHash(row.Id, row.At, row.Actor, row.Action, row.Target, row.Detail, row.Correlation, previous);
            using var command = writeConnection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE audit_log SET prev_hash=$prev, entry_hash=$hash WHERE rowid=$rowid";
            command.Parameters.AddWithValue("$prev", previous);
            command.Parameters.AddWithValue("$hash", hash);
            command.Parameters.AddWithValue("$rowid", row.RowId);
            command.ExecuteNonQuery();
            previous = hash;
        }

        transaction.Commit();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    // ---------------- Assessments ----------------

    public Task SaveAsync(AssessmentResult result, CancellationToken cancellationToken = default)
    {
        Execute(
            "INSERT OR REPLACE INTO assessments(id, tenant_id, run_utc, mode, overall, payload) VALUES ($id,$tenant,$run,$mode,$overall,$payload)",
            ("$id", result.Id.ToString()),
            ("$tenant", result.TenantId),
            ("$run", Iso(result.RunUtc)),
            ("$mode", result.Mode),
            ("$overall", result.Scores.Overall),
            ("$payload", JsonSerializer.Serialize(result, Json)));

        return Task.CompletedTask;
    }

    private Task<AssessmentResult?> GetLatestAssessmentAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var payload = Scalar(
            "SELECT payload FROM assessments WHERE tenant_id=$tenant ORDER BY run_utc DESC LIMIT 1",
            ("$tenant", tenantId));

        return Task.FromResult(Deserialize<AssessmentResult>(payload));
    }

    private Task<IReadOnlyList<AssessmentResult>> ListAssessmentsAsync(string tenantId, int take = 50, CancellationToken cancellationToken = default)
    {
        var results = new List<AssessmentResult>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM assessments WHERE tenant_id=$tenant ORDER BY run_utc DESC LIMIT $take";
        command.Parameters.AddWithValue("$tenant", tenantId);
        command.Parameters.AddWithValue("$take", take);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var item = Deserialize<AssessmentResult>(reader.GetString(0));
            if (item is not null) results.Add(item);
        }

        return Task.FromResult<IReadOnlyList<AssessmentResult>>(results);
    }

    // ---------------- Change plans ----------------

    public Task SaveAsync(ChangePlan plan, CancellationToken cancellationToken = default)
    {
        Execute(
            "INSERT OR REPLACE INTO change_plans(id, tenant_id, created_utc, state, payload) VALUES ($id,$tenant,$created,$state,$payload)",
            ("$id", plan.Id.ToString()),
            ("$tenant", plan.TenantId),
            ("$created", Iso(plan.CreatedUtc)),
            ("$state", "Planned"),
            ("$payload", JsonSerializer.Serialize(plan, Json)));

        return Task.CompletedTask;
    }

    public Task<ChangePlan?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Deserialize<ChangePlan>(Scalar("SELECT payload FROM change_plans WHERE id=$id", ("$id", id.ToString()))));

    private Task<IReadOnlyList<ChangePlan>> ListPlansAsync(string tenantId, int take = 50, CancellationToken cancellationToken = default)
    {
        var plans = new List<ChangePlan>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM change_plans WHERE tenant_id=$tenant ORDER BY created_utc DESC LIMIT $take";
        command.Parameters.AddWithValue("$tenant", tenantId);
        command.Parameters.AddWithValue("$take", take);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var plan = Deserialize<ChangePlan>(reader.GetString(0));
            if (plan is not null) plans.Add(plan);
        }

        return Task.FromResult<IReadOnlyList<ChangePlan>>(plans);
    }

    public Task UpdateStateAsync(Guid planId, string state, CancellationToken cancellationToken = default)
    {
        Execute("UPDATE change_plans SET state=$state WHERE id=$id", ("$state", state), ("$id", planId.ToString()));
        return Task.CompletedTask;
    }

    // ---------------- Approvals ----------------

    public Task SaveAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
    {
        Execute(
            "INSERT OR REPLACE INTO approvals(id, plan_id, state, updated_utc, payload) VALUES ($id,$plan,$state,$updated,$payload)",
            ("$id", request.Id.ToString()),
            ("$plan", request.PlanId.ToString()),
            ("$state", request.State.ToString()),
            ("$updated", Iso(request.UpdatedUtc)),
            ("$payload", JsonSerializer.Serialize(request, Json)));

        return Task.CompletedTask;
    }

    public Task<ApprovalRequest?> GetByPlanAsync(Guid planId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Deserialize<ApprovalRequest>(Scalar(
            "SELECT payload FROM approvals WHERE plan_id=$plan", ("$plan", planId.ToString()))));

    // ---------------- Backups ----------------

    public Task SaveAsync(ConfigurationBackup backup, CancellationToken cancellationToken = default)
    {
        Execute(
            "INSERT OR REPLACE INTO backups(id, tenant_id, created_utc, label, payload) VALUES ($id,$tenant,$created,$label,$payload)",
            ("$id", backup.Id.ToString()),
            ("$tenant", backup.TenantId),
            ("$created", Iso(backup.CreatedUtc)),
            ("$label", backup.Label),
            ("$payload", JsonSerializer.Serialize(backup, Json)));

        return Task.CompletedTask;
    }

    private Task<ConfigurationBackup?> GetLatestBackupAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Deserialize<ConfigurationBackup>(Scalar(
            "SELECT payload FROM backups WHERE tenant_id=$tenant ORDER BY created_utc DESC LIMIT 1",
            ("$tenant", tenantId))));

    private Task<IReadOnlyList<ConfigurationBackup>> ListBackupsAsync(string tenantId, int take = 20, CancellationToken cancellationToken = default)
    {
        var items = new List<ConfigurationBackup>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM backups WHERE tenant_id=$tenant ORDER BY created_utc DESC LIMIT $take";
        command.Parameters.AddWithValue("$tenant", tenantId);
        command.Parameters.AddWithValue("$take", take);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var item = Deserialize<ConfigurationBackup>(reader.GetString(0));
            if (item is not null) items.Add(item);
        }

        return Task.FromResult<IReadOnlyList<ConfigurationBackup>>(items);
    }

    // ---------------- Audit ----------------

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        lock (_auditGate)
        {
            var previous = Scalar("SELECT entry_hash FROM audit_log ORDER BY rowid DESC LIMIT 1") ?? string.Empty;
            var hash = AuditHash(entry.Id.ToString(), Iso(entry.AtUtc), entry.Actor, entry.Action,
                entry.Target, entry.Detail, entry.CorrelationId, previous);

            Execute(
                "INSERT INTO audit_log(id, at_utc, actor, action, target, detail, correlation_id, prev_hash, entry_hash) VALUES ($id,$at,$actor,$action,$target,$detail,$corr,$prev,$hash)",
                ("$id", entry.Id.ToString()),
                ("$at", Iso(entry.AtUtc)),
                ("$actor", entry.Actor),
                ("$action", entry.Action),
                ("$target", entry.Target),
                ("$detail", entry.Detail),
                ("$corr", entry.CorrelationId),
                ("$prev", previous),
                ("$hash", hash));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> ReadAsync(int take = 200, CancellationToken cancellationToken = default)
    {
        var entries = new List<AuditEntry>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, at_utc, actor, action, target, detail, correlation_id FROM audit_log ORDER BY at_utc DESC LIMIT $take";
        command.Parameters.AddWithValue("$take", take);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new AuditEntry(
                Guid.Parse(reader.GetString(0)),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return Task.FromResult<IReadOnlyList<AuditEntry>>(entries);
    }

    /// <summary>
    /// Recomputes the SHA-256 chain in insertion order. An edited row breaks the chain at that
    /// entry; a deleted row breaks it at the next one. This is tamper evidence, not prevention.
    /// </summary>
    public Task<AuditChainStatus> VerifyAsync(CancellationToken cancellationToken = default)
    {
        var previous = string.Empty;
        var verified = 0;

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, at_utc, actor, action, target, detail, correlation_id, prev_hash, entry_hash FROM audit_log ORDER BY rowid ASC";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var storedPrevious = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
            var storedHash = reader.IsDBNull(8) ? string.Empty : reader.GetString(8);

            if (storedHash.Length == 0)
            {
                return Task.FromResult(new AuditChainStatus(false, verified, id,
                    $"Entry {id} has no hash; the audit chain cannot be verified."));
            }

            var expected = AuditHash(id, reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), previous);

            if (!string.Equals(storedPrevious, previous, StringComparison.Ordinal) ||
                !string.Equals(storedHash, expected, StringComparison.Ordinal))
            {
                return Task.FromResult(new AuditChainStatus(false, verified, id,
                    $"Audit chain broken at entry {id}; the row or its predecessor was modified."));
            }

            previous = storedHash;
            verified++;
        }

        return Task.FromResult(new AuditChainStatus(true, verified, null,
            verified == 0 ? "Audit log is empty." : $"Audit chain valid across {verified} entr{(verified == 1 ? "y" : "ies")}."));
    }

    private static string AuditHash(string id, string at, string actor, string action, string target, string detail, string? correlation, string previous)
    {
        var canonical = string.Join('\u001f', id, at, actor, action, target, detail, correlation ?? string.Empty, previous);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    // ---------------- AI usage ----------------

    public Task RecordAsync(AiUsageRecord record, CancellationToken cancellationToken = default)
    {
        Execute(
            "INSERT INTO ai_usage(id, provider_id, model, prompt_tokens, completion_tokens, cost, succeeded, error_code, at_utc) VALUES ($id,$provider,$model,$prompt,$completion,$cost,$ok,$error,$at)",
            ("$id", record.Id.ToString()),
            ("$provider", record.ProviderId),
            ("$model", record.Model),
            ("$prompt", record.PromptTokens),
            ("$completion", record.CompletionTokens),
            ("$cost", record.EstimatedCostUsd),
            ("$ok", record.Succeeded ? 1 : 0),
            ("$error", record.ErrorCode),
            ("$at", Iso(record.AtUtc)));

        return Task.CompletedTask;
    }

    private Task<IReadOnlyList<AiUsageRecord>> ListUsageAsync(string? providerId = null, int take = 500, CancellationToken cancellationToken = default)
    {
        var records = new List<AiUsageRecord>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = providerId is null
            ? "SELECT id, provider_id, model, prompt_tokens, completion_tokens, cost, succeeded, error_code, at_utc FROM ai_usage ORDER BY at_utc DESC LIMIT $take"
            : "SELECT id, provider_id, model, prompt_tokens, completion_tokens, cost, succeeded, error_code, at_utc FROM ai_usage WHERE provider_id=$provider ORDER BY at_utc DESC LIMIT $take";

        command.Parameters.AddWithValue("$take", take);
        if (providerId is not null) command.Parameters.AddWithValue("$provider", providerId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(new AiUsageRecord(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetDouble(5),
                reader.GetInt32(6) == 1,
                reader.IsDBNull(7) ? null : reader.GetString(7),
                DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return Task.FromResult<IReadOnlyList<AiUsageRecord>>(records);
    }

    // ---------------- Settings ----------------

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var payload = Scalar("SELECT value FROM settings WHERE key='app'");
        var settings = Deserialize<AppSettings>(payload) ?? AppSettings.CreateDefault();

        if (settings.Providers.Count == 0)
        {
            settings.Providers = AppSettings.CreateDefault().Providers;
        }

        if (DpapiSecretProtector.IsProtected(settings.Auth.ClientSecret))
        {
            settings.Auth = settings.Auth with
            {
                ClientSecret = DpapiSecretProtector.UnprotectIfNeeded(settings.Auth.ClientSecret)
            };
        }

        return Task.FromResult(settings);
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        // The lab client secret never reaches the settings JSON in clear text. Provider keys are
        // not persisted at all; they resolve from environment variables at call time.
        var persisted = DpapiSecretProtector.IsProtected(settings.Auth.ClientSecret) ||
                        string.IsNullOrEmpty(settings.Auth.ClientSecret)
            ? settings
            : settings with
            {
                Auth = settings.Auth with { ClientSecret = DpapiSecretProtector.Protect(settings.Auth.ClientSecret!) }
            };

        Execute(
            "INSERT OR REPLACE INTO settings(key, value) VALUES ('app',$value)",
            ("$value", JsonSerializer.Serialize(persisted, Json)));

        return Task.CompletedTask;
    }

    // ---------------- Customer tenants (MSP) ----------------

    public Task SaveAsync(TenantProfile profile, CancellationToken cancellationToken = default)
    {
        var persisted = DpapiSecretProtector.IsProtected(profile.Auth.ClientSecret) ||
                        string.IsNullOrEmpty(profile.Auth.ClientSecret)
            ? profile
            : profile with
            {
                Auth = profile.Auth with { ClientSecret = DpapiSecretProtector.Protect(profile.Auth.ClientSecret!) }
            };

        Execute(
            "INSERT OR REPLACE INTO tenant_profiles(tenant_id, display_name, updated_utc, payload) VALUES ($id,$name,$updated,$payload)",
            ("$id", profile.TenantId),
            ("$name", profile.DisplayName),
            ("$updated", Iso(profile.UpdatedUtc)),
            ("$payload", JsonSerializer.Serialize(persisted, Json)));

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TenantProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        var profiles = new List<TenantProfile>();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM tenant_profiles ORDER BY display_name COLLATE NOCASE";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var profile = Deserialize<TenantProfile>(reader.GetString(0));
            if (profile is null) continue;

            if (DpapiSecretProtector.IsProtected(profile.Auth.ClientSecret))
            {
                profile = profile with
                {
                    Auth = profile.Auth with
                    {
                        ClientSecret = DpapiSecretProtector.UnprotectIfNeeded(profile.Auth.ClientSecret)
                    }
                };
            }

            profiles.Add(profile);
        }

        return Task.FromResult<IReadOnlyList<TenantProfile>>(profiles);
    }

    public Task DeleteAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        Execute("DELETE FROM tenant_profiles WHERE tenant_id=$id", ("$id", tenantId));
        return Task.CompletedTask;
    }

    // ---------------- Explicit interface implementations ----------------
    // Several ports share a shape but differ by return type, so they are bound explicitly.

    Task<AssessmentResult?> IAssessmentStore.GetLatestAsync(string tenantId, CancellationToken cancellationToken) =>
        GetLatestAssessmentAsync(tenantId, cancellationToken);

    Task<IReadOnlyList<AssessmentResult>> IAssessmentStore.ListAsync(string tenantId, int take, CancellationToken cancellationToken) =>
        ListAssessmentsAsync(tenantId, take, cancellationToken);

    Task<IReadOnlyList<ChangePlan>> IChangePlanStore.ListAsync(string tenantId, int take, CancellationToken cancellationToken) =>
        ListPlansAsync(tenantId, take, cancellationToken);

    Task<ConfigurationBackup?> IBackupStore.GetLatestAsync(string tenantId, CancellationToken cancellationToken) =>
        GetLatestBackupAsync(tenantId, cancellationToken);

    Task<IReadOnlyList<ConfigurationBackup>> IBackupStore.ListAsync(string tenantId, int take, CancellationToken cancellationToken) =>
        ListBackupsAsync(tenantId, take, cancellationToken);

    Task<IReadOnlyList<AiUsageRecord>> IAiUsageStore.ListAsync(string? providerId, int take, CancellationToken cancellationToken) =>
        ListUsageAsync(providerId, take, cancellationToken);

    // ---------------- Helpers ----------------

    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private string? Scalar(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var result = command.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToString(result, CultureInfo.InvariantCulture);
    }

    private static T? Deserialize<T>(string? payload) where T : class =>
        string.IsNullOrWhiteSpace(payload) ? null : JsonSerializer.Deserialize<T>(payload, Json);

    private static string Iso(DateTimeOffset value) => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
}
