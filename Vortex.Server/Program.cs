using System.Security.Claims;
using System.Text.Json;
using Vortex.Server.Data;
using Vortex.Server.Services;
using Vortex.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<VortexDb>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IAgentIsolationService, AgentIsolationService>();
if (builder.Configuration.GetValue<bool>("Hermes:UseFakeGateway", true)) builder.Services.AddSingleton<IHermesGatewayService, FakeHermesGatewayService>();
else builder.Services.AddSingleton<IHermesGatewayService, HermesGatewayService>();
builder.Services.AddScoped<IHermesProfileService, HermesProfileService>();
builder.Services.AddScoped<IAgentPolicyService, AgentPolicyService>();
builder.Services.AddScoped<IAgentUsageService, AgentUsageService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ModelRouter>();
builder.Services.AddScoped<AiProviderClient>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddHttpClient("ai-provider", client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();
app.UseCors();
await app.Services.GetRequiredService<VortexDb>().InitializeAsync();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Vortex.Server", utc = DateTimeOffset.UtcNow }));

app.MapPost("/api/auth/register", async (RegisterRequest request, AuthService auth, CancellationToken ct) =>
{
    try { return Results.Ok(await auth.RegisterAsync(request, ct)); }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

app.MapPost("/api/auth/login", async (LoginRequest request, AuthService auth, CancellationToken ct) =>
{
    try { return Results.Ok(await auth.LoginAsync(request, ct)); }
    catch { return Results.Unauthorized(); }
});

app.MapGet("/api/me", async (HttpContext context, AuthService auth, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    var profile = await auth.GetProfileAsync(userId.Value, ct);
    return profile is null ? Results.Unauthorized() : Results.Ok(profile);
});

app.MapPost("/api/agent/provision", async (HttpContext context, IHermesProfileService profiles, IAgentPolicyService policies, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    await profiles.EnsureProfileAsync(userId.Value, ct);
    return Results.Ok(await policies.GetStatusAsync(userId.Value, ct));
});

app.MapGet("/api/agent/status", async (HttpContext context, IAgentPolicyService policies, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    return userId is null ? Results.Unauthorized() : Results.Ok(await policies.GetStatusAsync(userId.Value, ct));
});

app.MapPost("/api/agent/chat", async (HttpContext context, AgentChatRequest request, IHermesProfileService profiles, IAgentPolicyService policies, IAgentUsageService usage, IHermesGatewayService gateway, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    var profile = await profiles.EnsureProfileAsync(userId.Value, ct);
    var policy = await policies.GetPolicyAsync(userId.Value, ct);
    var requestId = Guid.NewGuid().ToString("N");
    if (!await usage.CanStartRunAsync(userId.Value, policy, ct))
    {
        await usage.StartExecutionAsync(userId.Value, profile.Id, requestId, request.Model, true, ct);
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    var logId = await usage.StartExecutionAsync(userId.Value, profile.Id, requestId, request.Model, false, ct);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(policy.MaxRunSeconds));
    try
    {
        var response = await gateway.ChatAsync(profile, request, policy, timeout.Token);
        await usage.IncrementRunAsync(userId.Value, Math.Max(1, request.Message.Length / 4), Math.Max(1, response.Length / 4), 0, ct);
        await usage.FinishExecutionAsync(logId, AgentExecutionStatus.Succeeded, null, ct);
        var status = await policies.GetStatusAsync(userId.Value, ct);
        return Results.Ok(new AgentChatResponse(requestId, response, profile.HermesProfileName, status.RemainingRunsToday));
    }
    catch (OperationCanceledException)
    {
        await usage.FinishExecutionAsync(logId, AgentExecutionStatus.TimedOut, "Timeout", CancellationToken.None);
        return Results.StatusCode(StatusCodes.Status408RequestTimeout);
    }
    catch
    {
        await usage.FinishExecutionAsync(logId, AgentExecutionStatus.Failed, "HermesFailed", CancellationToken.None);
        return Results.Problem("Hermes isteği çalıştırılamadı.");
    }
});

app.MapGet("/api/agent/tasks", async (HttpContext context, VortexDb db, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    await using var connection = await db.OpenAsync(ct);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT Id, Name, Schedule, TimeZone, IsEnabled, CreatedAt FROM AgentScheduledTasks WHERE UserId = $userId ORDER BY CreatedAt DESC";
    command.Parameters.AddWithValue("$userId", userId.Value.ToString());
    var tasks = new List<AgentTaskDto>();
    await using var reader = await command.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct)) tasks.Add(new AgentTaskDto(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4) == 1, DateTimeOffset.Parse(reader.GetString(5))));
    return Results.Ok(tasks);
});

app.MapPost("/api/agent/tasks", async (HttpContext context, CreateAgentTaskRequest request, VortexDb db, IHermesProfileService profiles, IAgentPolicyService policies, IHermesGatewayService gateway, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    var status = await policies.GetStatusAsync(userId.Value, ct);
    if (status.ActiveScheduledTaskCount >= status.Policy.ActiveScheduledTaskLimit) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    var profile = await profiles.EnsureProfileAsync(userId.Value, ct);
    var externalId = await gateway.CreateScheduledTaskAsync(profile, request, ct);
    var id = Guid.NewGuid();
    await using var connection = await db.OpenAsync(ct);
    await VortexDb.ExecuteAsync(connection, "INSERT INTO AgentScheduledTasks (Id, UserId, AgentProfileId, ExternalHermesTaskId, Name, Schedule, TimeZone, IsEnabled, CreatedAt) VALUES ($id, $userId, $profileId, $externalId, $name, $schedule, $tz, 1, $createdAt)", ct, ("$id", id.ToString()), ("$userId", userId.Value.ToString()), ("$profileId", profile.Id.ToString()), ("$externalId", externalId), ("$name", request.Name), ("$schedule", request.Schedule), ("$tz", request.TimeZone), ("$createdAt", DateTimeOffset.UtcNow.ToString("O")));
    return Results.Ok(new AgentTaskDto(id, request.Name, request.Schedule, request.TimeZone, true, DateTimeOffset.UtcNow));
});

app.MapDelete("/api/agent/tasks/{id:guid}", async (HttpContext context, Guid id, VortexDb db, IHermesProfileService profiles, IHermesGatewayService gateway, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    await using var connection = await db.OpenAsync(ct);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT ExternalHermesTaskId FROM AgentScheduledTasks WHERE Id = $id AND UserId = $userId";
    command.Parameters.AddWithValue("$id", id.ToString());
    command.Parameters.AddWithValue("$userId", userId.Value.ToString());
    var externalId = Convert.ToString(await command.ExecuteScalarAsync(ct));
    if (externalId is null) return Results.NotFound();
    var profile = await profiles.EnsureProfileAsync(userId.Value, ct);
    await gateway.DeleteScheduledTaskAsync(profile, externalId, ct);
    await VortexDb.ExecuteAsync(connection, "DELETE FROM AgentScheduledTasks WHERE Id = $id AND UserId = $userId", ct, ("$id", id.ToString()), ("$userId", userId.Value.ToString()));
    return Results.NoContent();
});

app.MapGet("/api/chats", async (HttpContext context, string? q, ChatService chats, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    return userId is null ? Results.Unauthorized() : Results.Ok(await chats.ListSessionsAsync(userId.Value, q, ct));
});

app.MapPost("/api/chats", async (HttpContext context, CreateChatRequest request, ChatService chats, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    return userId is null ? Results.Unauthorized() : Results.Ok(await chats.CreateSessionAsync(userId.Value, request.Title, ct));
});

app.MapPut("/api/chats/{id:guid}", async (HttpContext context, Guid id, RenameChatRequest request, ChatService chats, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    await chats.RenameSessionAsync(userId.Value, id, request.Title, ct);
    return Results.NoContent();
});

app.MapDelete("/api/chats/{id:guid}", async (HttpContext context, Guid id, ChatService chats, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    await chats.DeleteSessionAsync(userId.Value, id, ct);
    return Results.NoContent();
});

app.MapPost("/api/chat/completions", async (HttpContext context, ChatCompletionRequest request, ChatService chats, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
    context.Response.ContentType = "text/event-stream; charset=utf-8";
    await foreach (var chunk in chats.CompleteAsync(userId.Value, request, ct))
    {
        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }
});

app.MapGet("/api/models", async (HttpContext context, VortexDb db, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    await using var connection = await db.OpenAsync(ct);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT Id, ProviderId, Name, DisplayName, IsPremium, SupportsStreaming, SupportsTools, ContextWindowTokens FROM AiModels WHERE IsActive = 1 ORDER BY DisplayName";
    var models = new List<AiModelDto>();
    await using var reader = await command.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct)) models.Add(new AiModelDto(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetInt32(4) == 1, reader.GetInt32(5) == 1, reader.GetInt32(6) == 1, reader.GetInt32(7)));
    return Results.Ok(models);
});

app.MapGet("/api/admin/usage", async (HttpContext context, VortexDb db, CancellationToken ct) =>
{
    if (!context.HasAnyRole(VortexRoles.Administrator, VortexRoles.Owner, VortexRoles.Support)) return Results.Forbid();
    await using var connection = await db.OpenAsync(ct);
    var total = await VortexDb.ScalarLongAsync(connection, "SELECT COUNT(*) FROM UsageRecords", ct);
    var failed = await VortexDb.ScalarLongAsync(connection, "SELECT COUNT(*) FROM UsageRecords WHERE Succeeded = 0", ct);
    return Results.Ok(new { totalRequests = total, failedRequests = failed });
});

app.Run();

public static class HttpContextAuthExtensions
{
    public static Guid? GetUserId(this HttpContext context)
    {
        var token = context.Request.Headers.Authorization.ToString();
        if (!token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var tokenService = context.RequestServices.GetRequiredService<TokenService>();
        var principal = tokenService.ValidateToken(token[7..]);
        var id = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out var value) ? value : null;
    }

    public static bool HasAnyRole(this HttpContext context, params string[] roles)
    {
        var token = context.Request.Headers.Authorization.ToString();
        if (!token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var tokenService = context.RequestServices.GetRequiredService<TokenService>();
        var principal = tokenService.ValidateToken(token[7..]);
        var role = principal?.FindFirstValue(ClaimTypes.Role);
        return role is not null && roles.Contains(role);
    }
}

public partial class Program { }
