using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Vortex.Server.Data;
using Vortex.Shared;

namespace Vortex.Server.Services;

public interface IAgentIsolationService
{
    string GetHermesHomePath(Guid userId);
    void EnsureHermesHomeLayout(string hermesHomePath);
    bool IsInsideUserWorkspace(Guid userId, string fullPath);
}

public sealed class AgentIsolationService(IConfiguration configuration, IWebHostEnvironment environment) : IAgentIsolationService
{
    public string GetHermesHomePath(Guid userId)
    {
        var root = configuration["Hermes:ProfilesRoot"];
        if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(environment.ContentRootPath, "App_Data", "hermes-profiles");
        return Path.Combine(Path.GetFullPath(root), userId.ToString("N"));
    }

    public void EnsureHermesHomeLayout(string hermesHomePath)
    {
        foreach (var child in new[] { "config", "memory", "sessions", "cron", "skills", "workspace", "logs" })
        {
            Directory.CreateDirectory(Path.Combine(hermesHomePath, child));
        }
    }

    public bool IsInsideUserWorkspace(Guid userId, string fullPath)
    {
        var workspace = Path.GetFullPath(Path.Combine(GetHermesHomePath(userId), "workspace"));
        var target = Path.GetFullPath(fullPath);
        return target.StartsWith(workspace, StringComparison.OrdinalIgnoreCase);
    }
}

public interface IHermesGatewayService
{
    Task ProvisionAsync(AgentProfileDto profile, CancellationToken cancellationToken);
    Task<string> ChatAsync(AgentProfileDto profile, AgentChatRequest request, AgentPolicyDto policy, CancellationToken cancellationToken);
    Task<string> CreateScheduledTaskAsync(AgentProfileDto profile, CreateAgentTaskRequest request, CancellationToken cancellationToken);
    Task DeleteScheduledTaskAsync(AgentProfileDto profile, string? externalTaskId, CancellationToken cancellationToken);
}

public sealed class FakeHermesGatewayService : IHermesGatewayService
{
    private static readonly ConcurrentDictionary<string, List<string>> MemoryByProfile = new();

    public Task ProvisionAsync(AgentProfileDto profile, CancellationToken cancellationToken)
    {
        MemoryByProfile.TryAdd(profile.HermesProfileName, []);
        return Task.CompletedTask;
    }

    public Task<string> ChatAsync(AgentProfileDto profile, AgentChatRequest request, AgentPolicyDto policy, CancellationToken cancellationToken)
    {
        var memory = MemoryByProfile.GetOrAdd(profile.HermesProfileName, []);
        if (request.Message.StartsWith("remember:", StringComparison.OrdinalIgnoreCase))
        {
            memory.Add(request.Message[9..].Trim());
            return Task.FromResult($"Stored in {profile.HermesProfileName}");
        }
        if (request.Message.Equals("recall", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(memory.Count == 0 ? "No memory" : string.Join(" | ", memory));
        }
        return Task.FromResult($"Hermes[{profile.HermesProfileName}] {request.Message}");
    }

    public Task<string> CreateScheduledTaskAsync(AgentProfileDto profile, CreateAgentTaskRequest request, CancellationToken cancellationToken)
        => Task.FromResult($"fake-{profile.Id:N}-{Guid.NewGuid():N}");

    public Task DeleteScheduledTaskAsync(AgentProfileDto profile, string? externalTaskId, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class HermesGatewayService(IConfiguration configuration, ILogger<HermesGatewayService> logger) : IHermesGatewayService
{
    public Task ProvisionAsync(AgentProfileDto profile, CancellationToken cancellationToken)
        => RunHermesAsync(["profile", "init", "--home", profile.HermesHomePath, "--profile", profile.HermesProfileName], profile, cancellationToken);

    public async Task<string> ChatAsync(AgentProfileDto profile, AgentChatRequest request, AgentPolicyDto policy, CancellationToken cancellationToken)
    {
        await RunHermesAsync(["agent", "chat", "--home", profile.HermesHomePath, "--profile", profile.HermesProfileName, "--timeout", policy.MaxRunSeconds.ToString(), "--message", request.Message], profile, cancellationToken);
        return "Hermes isteği tamamlandı.";
    }

    public async Task<string> CreateScheduledTaskAsync(AgentProfileDto profile, CreateAgentTaskRequest request, CancellationToken cancellationToken)
    {
        var externalId = Guid.NewGuid().ToString("N");
        await RunHermesAsync(["cron", "add", "--home", profile.HermesHomePath, "--profile", profile.HermesProfileName, "--id", externalId, "--name", request.Name, "--schedule", request.Schedule], profile, cancellationToken);
        return externalId;
    }

    public Task DeleteScheduledTaskAsync(AgentProfileDto profile, string? externalTaskId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalTaskId)) return Task.CompletedTask;
        return RunHermesAsync(["cron", "delete", "--home", profile.HermesHomePath, "--profile", profile.HermesProfileName, "--id", externalTaskId], profile, cancellationToken);
    }

    private async Task RunHermesAsync(string[] args, AgentProfileDto profile, CancellationToken cancellationToken)
    {
        var executable = configuration["Hermes:ExecutablePath"];
        if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("Hermes executable yapılandırılmamış.");
        using var process = new Process();
        process.StartInfo.FileName = executable;
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.WorkingDirectory = Path.Combine(profile.HermesHomePath, "workspace");
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.Start();
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            logger.LogWarning("Hermes failed for profile {ProfileId}. Error was redacted. Length={Length}", profile.Id, error.Length);
            throw new InvalidOperationException("Hermes işlemi başarısız oldu.");
        }
    }
}

public interface IHermesProfileService
{
    Task<AgentProfileDto> EnsureProfileAsync(Guid userId, CancellationToken cancellationToken);
    Task<AgentProfileDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class HermesProfileService(VortexDb db, IAgentIsolationService isolation, IHermesGatewayService gateway, ILogger<HermesProfileService> logger) : IHermesProfileService
{
    public async Task<AgentProfileDto> EnsureProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existing = await GetProfileAsync(userId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == HermesProfileStatus.ProvisioningFailed.ToString()) await TryProvisionAsync(existing, cancellationToken);
            return await GetProfileAsync(userId, cancellationToken) ?? existing;
        }

        var now = DateTimeOffset.UtcNow;
        var profile = new AgentProfileDto(Guid.NewGuid(), userId, $"hermes-{userId:N}", isolation.GetHermesHomePath(userId), HermesProfileStatus.Provisioning.ToString(), now, null);
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "INSERT INTO UserAgentProfiles (Id, UserId, HermesProfileName, HermesHomePath, Status, CreatedAt, LastStartedAt) VALUES ($id, $userId, $name, $home, $status, $createdAt, NULL)", cancellationToken, ("$id", profile.Id.ToString()), ("$userId", userId.ToString()), ("$name", profile.HermesProfileName), ("$home", profile.HermesHomePath), ("$status", profile.Status), ("$createdAt", now.ToString("O")));
        await TryProvisionAsync(profile, cancellationToken);
        return await GetProfileAsync(userId, cancellationToken) ?? profile;
    }

    public async Task<AgentProfileDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, UserId, HermesProfileName, HermesHomePath, Status, CreatedAt, LastStartedAt FROM UserAgentProfiles WHERE UserId = $userId";
        command.Parameters.AddWithValue("$userId", userId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new AgentProfileDto(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4), DateTimeOffset.Parse(reader.GetString(5)), reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)));
    }

    private async Task TryProvisionAsync(AgentProfileDto profile, CancellationToken cancellationToken)
    {
        var status = HermesProfileStatus.Ready.ToString();
        try
        {
            isolation.EnsureHermesHomeLayout(profile.HermesHomePath);
            await gateway.ProvisionAsync(profile, cancellationToken);
        }
        catch (Exception ex)
        {
            status = HermesProfileStatus.ProvisioningFailed.ToString();
            logger.LogWarning(ex, "Hermes profile provisioning failed for {UserId}; secrets redacted", profile.UserId);
        }
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "UPDATE UserAgentProfiles SET Status = $status, LastStartedAt = $lastStartedAt WHERE Id = $id", cancellationToken, ("$status", status), ("$lastStartedAt", DateTimeOffset.UtcNow.ToString("O")), ("$id", profile.Id.ToString()));
    }
}

public interface IAgentPolicyService
{
    Task<AgentPolicyDto> GetPolicyAsync(Guid userId, CancellationToken cancellationToken);
    Task<AgentStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class AgentPolicyService(VortexDb db, IHermesProfileService profiles) : IAgentPolicyService
{
    public async Task<AgentPolicyDto> GetPolicyAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pap.DailyAgentRunLimit, pap.ActiveScheduledTaskLimit, pap.PersistentMemoryLimit, pap.IsSubAgentEnabled, pap.IsTerminalEnabled, pap.IsSystemCommandEnabled, pap.MaxRunSeconds, pap.MaxConcurrentRuns, pap.FileAccessScope FROM Users u JOIN PlanAgentPolicies pap ON pap.PlanId = u.PlanId WHERE u.Id = $userId";
        command.Parameters.AddWithValue("$userId", userId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Agent policy bulunamadı.");
        return new AgentPolicyDto(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3) == 1, reader.GetInt32(4) == 1, reader.GetInt32(5) == 1, reader.GetInt32(6), reader.GetInt32(7), reader.GetString(8));
    }

    public async Task<AgentStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var policy = await GetPolicyAsync(userId, cancellationToken);
        var profile = await profiles.GetProfileAsync(userId, cancellationToken);
        await using var connection = await db.OpenAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var runs = (int)await VortexDb.ScalarLongAsync(connection, "SELECT COALESCE(MAX(AgentRuns), 0) FROM AgentUsageCounters WHERE UserId = $userId AND Date = $date", cancellationToken, ("$userId", userId.ToString()), ("$date", today.ToString("O")));
        var activeTasks = (int)await VortexDb.ScalarLongAsync(connection, "SELECT COUNT(*) FROM AgentScheduledTasks WHERE UserId = $userId AND IsEnabled = 1", cancellationToken, ("$userId", userId.ToString()));
        var usage = new AgentUsageDto(today, runs, 0, 0, 0, DateTimeOffset.UtcNow);
        return new AgentStatusDto(profile, policy, usage, Math.Max(0, policy.DailyAgentRunLimit - runs), activeTasks, Math.Max(0, policy.ActiveScheduledTaskLimit - activeTasks));
    }
}

public interface IAgentUsageService
{
    Task<bool> CanStartRunAsync(Guid userId, AgentPolicyDto policy, CancellationToken cancellationToken);
    Task IncrementRunAsync(Guid userId, int inputTokens, int outputTokens, decimal estimatedCost, CancellationToken cancellationToken);
    Task<Guid> StartExecutionAsync(Guid userId, Guid? profileId, string requestId, string? model, bool wasLimitRejected, CancellationToken cancellationToken);
    Task FinishExecutionAsync(Guid logId, AgentExecutionStatus status, string? errorCode, CancellationToken cancellationToken);
}

public sealed class AgentUsageService(VortexDb db) : IAgentUsageService
{
    public async Task<bool> CanStartRunAsync(Guid userId, AgentPolicyDto policy, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).ToString("O");
        var runs = await VortexDb.ScalarLongAsync(connection, "SELECT COALESCE(MAX(AgentRuns), 0) FROM AgentUsageCounters WHERE UserId = $userId AND Date = $date", cancellationToken, ("$userId", userId.ToString()), ("$date", today));
        var active = await VortexDb.ScalarLongAsync(connection, "SELECT COUNT(*) FROM AgentExecutionLogs WHERE UserId = $userId AND Status = 'Started'", cancellationToken, ("$userId", userId.ToString()));
        return runs < policy.DailyAgentRunLimit && active < policy.MaxConcurrentRuns;
    }

    public async Task IncrementRunAsync(Guid userId, int inputTokens, int outputTokens, decimal estimatedCost, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).ToString("O");
        var updatedAt = DateTimeOffset.UtcNow.ToString("O");
        await VortexDb.ExecuteAsync(connection, "INSERT INTO AgentUsageCounters (Id, UserId, Date, AgentRuns, InputTokens, OutputTokens, EstimatedCost, UpdatedAt) VALUES ($id, $userId, $date, 1, $input, $output, $cost, $updatedAt) ON CONFLICT(UserId, Date) DO UPDATE SET AgentRuns = AgentRuns + 1, InputTokens = InputTokens + $input, OutputTokens = OutputTokens + $output, EstimatedCost = EstimatedCost + $cost, UpdatedAt = $updatedAt", cancellationToken, ("$id", Guid.NewGuid().ToString()), ("$userId", userId.ToString()), ("$date", today), ("$input", inputTokens), ("$output", outputTokens), ("$cost", estimatedCost), ("$updatedAt", updatedAt));
    }

    public async Task<Guid> StartExecutionAsync(Guid userId, Guid? profileId, string requestId, string? model, bool wasLimitRejected, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "INSERT INTO AgentExecutionLogs (Id, UserId, AgentProfileId, RequestId, StartedAt, FinishedAt, Status, ErrorCode, Model, WasLimitRejected) VALUES ($id, $userId, $profileId, $requestId, $startedAt, NULL, $status, NULL, $model, $rejected)", cancellationToken, ("$id", id.ToString()), ("$userId", userId.ToString()), ("$profileId", profileId?.ToString()), ("$requestId", requestId), ("$startedAt", DateTimeOffset.UtcNow.ToString("O")), ("$status", wasLimitRejected ? AgentExecutionStatus.LimitRejected.ToString() : AgentExecutionStatus.Started.ToString()), ("$model", model), ("$rejected", wasLimitRejected ? 1 : 0));
        return id;
    }

    public async Task FinishExecutionAsync(Guid logId, AgentExecutionStatus status, string? errorCode, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "UPDATE AgentExecutionLogs SET Status = $status, ErrorCode = $errorCode, FinishedAt = $finishedAt WHERE Id = $id", cancellationToken, ("$status", status.ToString()), ("$errorCode", errorCode), ("$finishedAt", DateTimeOffset.UtcNow.ToString("O")), ("$id", logId.ToString()));
    }
}
