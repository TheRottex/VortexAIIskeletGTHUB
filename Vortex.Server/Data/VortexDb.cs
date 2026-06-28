using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Vortex.Server.Data;

public sealed class VortexDb
{
    private readonly string _connectionString;

    public VortexDb(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var dataDir = configuration["Vortex:DataDirectory"];
        if (string.IsNullOrWhiteSpace(dataDir)) dataDir = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDir);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDir, "vortex.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        foreach (var sql in SchemaStatements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await SeedPlansAsync(connection, cancellationToken);
        await SeedProvidersAsync(connection, cancellationToken);
    }

    private static async Task SeedPlansAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var count = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM SubscriptionPlans", cancellationToken);
        if (count > 0) return;
        var freePlanId = Guid.NewGuid().ToString();
        var paidPlanId = Guid.NewGuid().ToString();
        await ExecuteAsync(connection, "INSERT INTO SubscriptionPlans (Id, Name, DisplayName, StorageQuotaBytes, DailyRequestLimit, MonthlyRequestLimit, IsActive) VALUES ($id, 'free', 'Ücretsiz Plan', 1073741824, 50, 1000, 1)", cancellationToken, ("$id", freePlanId));
        await ExecuteAsync(connection, "INSERT INTO SubscriptionPlans (Id, Name, DisplayName, StorageQuotaBytes, DailyRequestLimit, MonthlyRequestLimit, IsActive) VALUES ($id, 'paid', 'Ücretli Plan', 53687091200, 500, 15000, 1)", cancellationToken, ("$id", paidPlanId));
        foreach (var feature in new[] { "chat", "premium-chat", "file-context", "voice-input", "text-to-speech", "local-tools" })
        {
            await ExecuteAsync(connection, "INSERT INTO FeatureEntitlements (Id, PlanId, FeatureName, IsEnabled, LimitValue, RequiresConfirmation) VALUES ($id, $planId, $feature, 1, $limit, $confirmation)", cancellationToken, ("$id", Guid.NewGuid().ToString()), ("$planId", freePlanId), ("$feature", feature), ("$limit", feature == "premium-chat" ? 5 : 20), ("$confirmation", feature == "local-tools" ? 1 : 0));
            await ExecuteAsync(connection, "INSERT INTO FeatureEntitlements (Id, PlanId, FeatureName, IsEnabled, LimitValue, RequiresConfirmation) VALUES ($id, $planId, $feature, 1, $limit, $confirmation)", cancellationToken, ("$id", Guid.NewGuid().ToString()), ("$planId", paidPlanId), ("$feature", feature), ("$limit", feature == "premium-chat" ? 20 : 200), ("$confirmation", feature == "local-tools" ? 1 : 0));
        }
    }

    private static async Task SeedProvidersAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var count = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM AiProviders", cancellationToken);
        if (count > 0) return;
        var providerId = Guid.NewGuid().ToString();
        var modelId = Guid.NewGuid().ToString();
        await ExecuteAsync(connection, "INSERT INTO AiProviders (Id, Name, ProviderType, BaseUrl, EncryptedApiKey, IsActive, Priority) VALUES ($id, 'OpenAI Compatible Demo', 'openai-compatible', 'https://api.openai.com', NULL, 0, 100)", cancellationToken, ("$id", providerId));
        await ExecuteAsync(connection, "INSERT INTO AiModels (Id, ProviderId, Name, DisplayName, IsPremium, SupportsStreaming, SupportsTools, ContextWindowTokens, InputCostPerMillion, OutputCostPerMillion, IsActive) VALUES ($id, $providerId, 'gpt-4o-mini', 'OpenAI Compatible / gpt-4o-mini', 0, 1, 1, 128000, 0.15, 0.60, 1)", cancellationToken, ("$id", modelId), ("$providerId", providerId));
        await using var planCommand = connection.CreateCommand();
        planCommand.CommandText = "SELECT Id FROM SubscriptionPlans";
        await using var reader = await planCommand.ExecuteReaderAsync(cancellationToken);
        var planIds = new List<string>();
        while (await reader.ReadAsync(cancellationToken)) planIds.Add(reader.GetString(0));
        foreach (var planId in planIds)
        {
            await ExecuteAsync(connection, "INSERT INTO PlanModelPolicies (Id, PlanId, ProviderId, ModelId, Priority, DailyUsageLimit, MonthlyUsageLimit, FeatureName, FallbackProviderId, FallbackModelId, IsActive) VALUES ($id, $planId, $providerId, $modelId, 100, 20, 1000, 'chat', NULL, NULL, 1)", cancellationToken, ("$id", Guid.NewGuid().ToString()), ("$planId", planId), ("$providerId", providerId), ("$modelId", modelId));
        }
    }

    public static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value ?? 0);
    }

    public static string HashSecret(string secret, string salt) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + secret)));

    private static readonly string[] SchemaStatements =
    [
        "CREATE TABLE IF NOT EXISTS SubscriptionPlans (Id TEXT PRIMARY KEY, Name TEXT NOT NULL UNIQUE, DisplayName TEXT NOT NULL, StorageQuotaBytes INTEGER NOT NULL, DailyRequestLimit INTEGER NOT NULL, MonthlyRequestLimit INTEGER NOT NULL, IsActive INTEGER NOT NULL);",
        "CREATE TABLE IF NOT EXISTS Users (Id TEXT PRIMARY KEY, Email TEXT NOT NULL UNIQUE, DisplayName TEXT NOT NULL, PasswordHash TEXT NOT NULL, PasswordSalt TEXT NOT NULL, Role TEXT NOT NULL, PlanId TEXT NOT NULL, StorageUsedBytes INTEGER NOT NULL DEFAULT 0, CreatedAt TEXT NOT NULL, FOREIGN KEY (PlanId) REFERENCES SubscriptionPlans(Id));",
        "CREATE TABLE IF NOT EXISTS FeatureEntitlements (Id TEXT PRIMARY KEY, PlanId TEXT NOT NULL, FeatureName TEXT NOT NULL, IsEnabled INTEGER NOT NULL, LimitValue INTEGER NULL, RequiresConfirmation INTEGER NOT NULL, FOREIGN KEY (PlanId) REFERENCES SubscriptionPlans(Id));",
        "CREATE TABLE IF NOT EXISTS AiProviders (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, ProviderType TEXT NOT NULL, BaseUrl TEXT NOT NULL, EncryptedApiKey TEXT NULL, IsActive INTEGER NOT NULL, Priority INTEGER NOT NULL, LastError TEXT NULL);",
        "CREATE TABLE IF NOT EXISTS AiModels (Id TEXT PRIMARY KEY, ProviderId TEXT NOT NULL, Name TEXT NOT NULL, DisplayName TEXT NOT NULL, IsPremium INTEGER NOT NULL, SupportsStreaming INTEGER NOT NULL, SupportsTools INTEGER NOT NULL, ContextWindowTokens INTEGER NOT NULL, InputCostPerMillion REAL NOT NULL, OutputCostPerMillion REAL NOT NULL, IsActive INTEGER NOT NULL, FOREIGN KEY (ProviderId) REFERENCES AiProviders(Id));",
        "CREATE TABLE IF NOT EXISTS PlanModelPolicies (Id TEXT PRIMARY KEY, PlanId TEXT NOT NULL, ProviderId TEXT NOT NULL, ModelId TEXT NOT NULL, Priority INTEGER NOT NULL, DailyUsageLimit INTEGER NOT NULL, MonthlyUsageLimit INTEGER NOT NULL, FeatureName TEXT NOT NULL, FallbackProviderId TEXT NULL, FallbackModelId TEXT NULL, IsActive INTEGER NOT NULL, FOREIGN KEY (PlanId) REFERENCES SubscriptionPlans(Id), FOREIGN KEY (ProviderId) REFERENCES AiProviders(Id), FOREIGN KEY (ModelId) REFERENCES AiModels(Id));",
        "CREATE TABLE IF NOT EXISTS ChatSessions (Id TEXT PRIMARY KEY, UserId TEXT NOT NULL, Title TEXT NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, FOREIGN KEY (UserId) REFERENCES Users(Id));",
        "CREATE TABLE IF NOT EXISTS ChatMessages (Id TEXT PRIMARY KEY, ChatSessionId TEXT NOT NULL, Role TEXT NOT NULL, Content TEXT NOT NULL, CreatedAt TEXT NOT NULL, ModelName TEXT NULL, IsStreaming INTEGER NOT NULL, ErrorMessage TEXT NULL, FOREIGN KEY (ChatSessionId) REFERENCES ChatSessions(Id) ON DELETE CASCADE);",
        "CREATE TABLE IF NOT EXISTS UsageRecords (Id TEXT PRIMARY KEY, UserId TEXT NOT NULL, PlanName TEXT NOT NULL, ProviderName TEXT NOT NULL, ModelName TEXT NOT NULL, InputTokens INTEGER NOT NULL, OutputTokens INTEGER NOT NULL, EstimatedCost REAL NOT NULL, CreatedAt TEXT NOT NULL, LatencyMs INTEGER NOT NULL, Succeeded INTEGER NOT NULL, UsedFallback INTEGER NOT NULL, FeatureName TEXT NOT NULL, CorrelationId TEXT NOT NULL);",
        "CREATE TABLE IF NOT EXISTS AuditLogs (Id TEXT PRIMARY KEY, UserId TEXT NULL, Action TEXT NOT NULL, Details TEXT NOT NULL, CreatedAt TEXT NOT NULL, CorrelationId TEXT NOT NULL);"
    ];
}
