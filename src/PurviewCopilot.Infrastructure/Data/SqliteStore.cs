using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;

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
    IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

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

            CREATE TABLE IF NOT EXISTS baselines (
                tenant_id TEXT PRIMARY KEY,
                updated_utc TEXT NOT NULL,
                payload TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
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
        Execute(
            "INSERT INTO audit_log(id, at_utc, actor, action, target, detail, correlation_id) VALUES ($id,$at,$actor,$action,$target,$detail,$corr)",
            ("$id", entry.Id.ToString()),
            ("$at", Iso(entry.AtUtc)),
            ("$actor", entry.Actor),
            ("$action", entry.Action),
            ("$target", entry.Target),
            ("$detail", entry.Detail),
            ("$corr", entry.CorrelationId));

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

        return Task.FromResult(settings);
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Execute(
            "INSERT OR REPLACE INTO settings(key, value) VALUES ('app',$value)",
            ("$value", JsonSerializer.Serialize(settings, Json)));

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
