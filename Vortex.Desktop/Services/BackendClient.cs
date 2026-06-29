using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Vortex.Shared;

namespace Vortex.Desktop.Services;

public sealed record ExchangeResult(HttpStatusCode StatusCode, string SafeBody, AuthResponse? AuthResponse);

public sealed class BackendClient(HttpClient httpClient, TokenStorageService tokenStorage)
{
    private string? _token;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SetTokenAsync(string token, CancellationToken cancellationToken)
    {
        _token = token;
        await tokenStorage.SaveAsync(token, cancellationToken);
    }

    public async Task<bool> TryLoadStoredTokenAsync(CancellationToken cancellationToken)
    {
        _token = await tokenStorage.LoadAsync(cancellationToken);
        return !string.IsNullOrWhiteSpace(_token);
    }

    public void Logout()
    {
        _token = null;
        tokenStorage.Clear();
    }

    public async Task<UserProfileDto?> GetMeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_token)) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<UserProfileDto>(stream, JsonOptions, cancellationToken);
    }

    public async Task<AgentStatusDto?> GetAgentStatusAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_token)) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/agent/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<AgentStatusDto>(stream, JsonOptions, cancellationToken);
    }

    public async Task<StartDesktopAuthResponse?> StartDesktopAuthAsync(StartDesktopAuthRequest payload, CancellationToken cancellationToken)
        => await PostAsync<StartDesktopAuthRequest, StartDesktopAuthResponse>("/api/desktop-auth/sessions", payload, cancellationToken);

    public async Task<ExchangeResult> ExchangeDesktopCodeDetailedAsync(ExchangeDesktopCodeRequest payload, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync("/api/desktop-auth/token", new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"), cancellationToken);
        var safeBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (safeBody.Length > 500) safeBody = safeBody[..500];
        AuthResponse? auth = null;
        if (response.IsSuccessStatusCode)
        {
            auth = JsonSerializer.Deserialize<AuthResponse>(safeBody, JsonOptions);
        }
        return new ExchangeResult(response.StatusCode, safeBody, auth);
    }

    public async Task<AuthResponse?> ExchangeDesktopCodeAsync(ExchangeDesktopCodeRequest payload, CancellationToken cancellationToken)
        => (await ExchangeDesktopCodeDetailedAsync(payload, cancellationToken)).AuthResponse;

    public async IAsyncEnumerable<ChatCompletionChunk> StreamChatAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_token)) yield break;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Content = new StringContent(JsonSerializer.Serialize(new ChatCompletionRequest(null, message, null, null, null, true), JsonOptions), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(line[5..].Trim(), JsonOptions);
            if (chunk is not null) yield return chunk;
        }
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest payload, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(path, new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"), cancellationToken);
        if (!response.IsSuccessStatusCode) return default;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonOptions, cancellationToken);
    }
}
