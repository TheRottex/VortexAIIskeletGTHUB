using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Vortex.Server.Data;
using Vortex.Shared;

namespace Vortex.Server.Services;

public sealed record RoutedModel(Guid ProviderId, Guid ModelId, string ProviderName, string ProviderType, string BaseUrl, string? ApiKey, string ModelName, string DisplayName, bool IsMock);

public sealed class ModelRouter(VortexDb db)
{
    public async Task<RoutedModel> RouteAsync(Guid userId, string featureName, string? requestedModel, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT p.ProviderId, p.ModelId, pr.Name, pr.ProviderType, pr.BaseUrl, pr.EncryptedApiKey, m.Name, m.DisplayName, pr.IsActive FROM Users u JOIN PlanModelPolicies p ON p.PlanId = u.PlanId AND p.IsActive = 1 AND p.FeatureName = $feature JOIN AiProviders pr ON pr.Id = p.ProviderId JOIN AiModels m ON m.Id = p.ModelId AND m.IsActive = 1 WHERE u.Id = $userId AND ($requested IS NULL OR m.Name = $requested OR m.DisplayName = $requested) ORDER BY pr.IsActive DESC, p.Priority ASC, pr.Priority ASC LIMIT 1";
        command.Parameters.AddWithValue("$userId", userId.ToString());
        command.Parameters.AddWithValue("$feature", featureName);
        command.Parameters.AddWithValue("$requested", string.IsNullOrWhiteSpace(requestedModel) ? DBNull.Value : requestedModel);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new RoutedModel(Guid.Empty, Guid.Empty, "Mock Provider", "mock", "http://localhost", null, requestedModel ?? "vortex-demo-model", "Vortex Demo Model", true);
        return new RoutedModel(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetInt32(8) == 0 || reader.IsDBNull(5));
    }
}

public sealed class AiProviderClient(IHttpClientFactory httpClientFactory, ILogger<AiProviderClient> logger)
{
    public async IAsyncEnumerable<string> StreamChatAsync(RoutedModel route, IReadOnlyList<AiChatMessage> messages, string correlationId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (route.IsMock || string.IsNullOrWhiteSpace(route.ApiKey))
        {
            var text = "Vortex backend çalışıyor. Gerçek sağlayıcı anahtarı admin panelinden eklendiğinde yanıtlar resmi API üzerinden stream edilecek.";
            foreach (var word in text.Split(' '))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(35, cancellationToken);
                yield return word + " ";
            }
            yield break;
        }

        var client = httpClientFactory.CreateClient("ai-provider");
        using var request = new HttpRequestMessage(HttpMethod.Post, route.BaseUrl.TrimEnd('/') + "/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", route.ApiKey);
        request.Headers.Add("X-Correlation-Id", correlationId);
        request.Content = new StringContent(JsonSerializer.Serialize(new { model = route.ModelName, stream = true, messages = messages.Select(m => new { role = m.Role, content = m.Content }) }), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("AI provider returned {StatusCode} for correlation {CorrelationId}", response.StatusCode, correlationId);
            yield return $"Sağlayıcı şu anda yanıt veremiyor ({(int)response.StatusCode}).";
            yield break;
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") yield break;
            var delta = TryReadDelta(data);
            if (!string.IsNullOrEmpty(delta)) yield return delta;
        }
    }

    private static string? TryReadDelta(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("delta").TryGetProperty("content", out var content) ? content.GetString() : null;
        }
        catch { return null; }
    }
}

public sealed class ChatService(VortexDb db, ModelRouter router, AiProviderClient aiClient)
{
    public async Task<IReadOnlyList<ChatSessionDto>> ListSessionsAsync(Guid userId, string? query, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title, CreatedAt, UpdatedAt FROM ChatSessions WHERE UserId = $userId AND ($query IS NULL OR Title LIKE '%' || $query || '%') ORDER BY UpdatedAt DESC";
        command.Parameters.AddWithValue("$userId", userId.ToString());
        command.Parameters.AddWithValue("$query", string.IsNullOrWhiteSpace(query) ? DBNull.Value : query);
        var sessions = new List<ChatSessionDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) sessions.Add(new ChatSessionDto(Guid.Parse(reader.GetString(0)), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2)), DateTimeOffset.Parse(reader.GetString(3))));
        return sessions;
    }

    public async Task<ChatSessionDto> CreateSessionAsync(Guid userId, string? title, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new ChatSessionDto(Guid.NewGuid(), string.IsNullOrWhiteSpace(title) ? "Yeni sohbet" : title.Trim(), now, now);
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "INSERT INTO ChatSessions (Id, UserId, Title, CreatedAt, UpdatedAt) VALUES ($id, $userId, $title, $createdAt, $updatedAt)", cancellationToken, ("$id", session.Id.ToString()), ("$userId", userId.ToString()), ("$title", session.Title), ("$createdAt", now.ToString("O")), ("$updatedAt", now.ToString("O")));
        return session;
    }

    public async Task RenameSessionAsync(Guid userId, Guid sessionId, string title, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "UPDATE ChatSessions SET Title = $title, UpdatedAt = $updatedAt WHERE Id = $id AND UserId = $userId", cancellationToken, ("$title", title.Trim()), ("$updatedAt", DateTimeOffset.UtcNow.ToString("O")), ("$id", sessionId.ToString()), ("$userId", userId.ToString()));
    }

    public async Task DeleteSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "DELETE FROM ChatSessions WHERE Id = $id AND UserId = $userId", cancellationToken, ("$id", sessionId.ToString()), ("$userId", userId.ToString()));
    }

    public async IAsyncEnumerable<ChatCompletionChunk> CompleteAsync(Guid userId, ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var sessionId = request.ChatSessionId ?? (await CreateSessionAsync(userId, AutoTitle(request.Message), cancellationToken)).Id;
        await AppendMessageAsync(sessionId, "user", request.Message, null, false, null, cancellationToken);
        var route = await router.RouteAsync(userId, VortexFeatures.Chat, request.RequestedModel, cancellationToken);
        var system = string.IsNullOrWhiteSpace(request.SystemPrompt) ? "Sen Vortex AI Assistant içindeki güvenli yardımcı asistansın." : request.SystemPrompt;
        var messages = new List<AiChatMessage> { new("system", system), new("user", BuildPrompt(request)) };
        var stopwatch = Stopwatch.StartNew();
        var response = new StringBuilder();
        await foreach (var delta in aiClient.StreamChatAsync(route, messages, correlationId, cancellationToken))
        {
            response.Append(delta);
            yield return new ChatCompletionChunk(delta, false, route.DisplayName, null, correlationId);
        }
        stopwatch.Stop();
        await AppendMessageAsync(sessionId, "assistant", response.ToString(), route.DisplayName, false, null, cancellationToken);
        await RecordUsageAsync(userId, route, response.Length, stopwatch.Elapsed, true, correlationId, cancellationToken);
        yield return new ChatCompletionChunk(string.Empty, true, route.DisplayName, null, correlationId);
    }

    private async Task AppendMessageAsync(Guid sessionId, string role, string content, string? modelName, bool isStreaming, string? error, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "INSERT INTO ChatMessages (Id, ChatSessionId, Role, Content, CreatedAt, ModelName, IsStreaming, ErrorMessage) VALUES ($id, $sessionId, $role, $content, $createdAt, $modelName, $isStreaming, $error)", cancellationToken, ("$id", Guid.NewGuid().ToString()), ("$sessionId", sessionId.ToString()), ("$role", role), ("$content", content), ("$createdAt", DateTimeOffset.UtcNow.ToString("O")), ("$modelName", modelName), ("$isStreaming", isStreaming ? 1 : 0), ("$error", error));
    }

    private async Task RecordUsageAsync(Guid userId, RoutedModel route, int outputSize, TimeSpan latency, bool succeeded, string correlationId, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "INSERT INTO UsageRecords (Id, UserId, PlanName, ProviderName, ModelName, InputTokens, OutputTokens, EstimatedCost, CreatedAt, LatencyMs, Succeeded, UsedFallback, FeatureName, CorrelationId) VALUES ($id, $userId, 'current', $provider, $model, 0, $tokens, 0, $createdAt, $latency, $succeeded, 0, 'chat', $correlationId)", cancellationToken, ("$id", Guid.NewGuid().ToString()), ("$userId", userId.ToString()), ("$provider", route.ProviderName), ("$model", route.DisplayName), ("$tokens", Math.Max(1, outputSize / 4)), ("$createdAt", DateTimeOffset.UtcNow.ToString("O")), ("$latency", (int)latency.TotalMilliseconds), ("$succeeded", succeeded ? 1 : 0), ("$correlationId", correlationId));
    }

    private static string BuildPrompt(ChatCompletionRequest request)
    {
        var builder = new StringBuilder(request.Message);
        if (request.Files is { Count: > 0 })
        {
            builder.AppendLine("\n\nEklenen dosya bağlamı:");
            foreach (var file in request.Files.Where(f => !string.IsNullOrWhiteSpace(f.ExtractedText)))
            {
                builder.AppendLine($"--- {file.FileName} ({file.SizeBytes} bytes) ---");
                builder.AppendLine(file.ExtractedText);
            }
        }
        return builder.ToString();
    }

    private static string AutoTitle(string message) => message.Length <= 40 ? message : message[..40] + "…";
}
