using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Vortex.Shared;

namespace Vortex.Desktop.Services;

public sealed class BackendClient(HttpClient httpClient)
{
    private string? _token;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AuthResponse?> RegisterOrLoginDevelopmentUserAsync(CancellationToken cancellationToken)
    {
        var login = new LoginRequest("owner@vortex.local", "ChangeMe123!");
        var auth = await PostAsync<LoginRequest, AuthResponse>("/api/auth/login", login, cancellationToken);
        if (auth is null)
        {
            auth = await PostAsync<RegisterRequest, AuthResponse>("/api/auth/register", new RegisterRequest(login.Email, login.Password, "Vortex Owner"), cancellationToken);
        }
        _token = auth?.AccessToken;
        return auth;
    }

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
        try
        {
            using var response = await httpClient.PostAsync(path, new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"), cancellationToken);
            if (!response.IsSuccessStatusCode) return default;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonOptions, cancellationToken);
        }
        catch
        {
            return default;
        }
    }
}
