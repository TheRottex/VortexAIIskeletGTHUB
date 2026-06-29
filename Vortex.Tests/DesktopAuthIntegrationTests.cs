using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Vortex.Shared;

namespace Vortex.Tests;

public sealed class DesktopAuthIntegrationTests
{
    [Fact]
    public async Task DesktopAuthorizationCodePkceFlow_IsSingleUseAndUserBound()
    {
        using var factory = new DesktopAuthServerFactory();
        using var client = factory.CreateClient();

        var state = "state-a" + Guid.NewGuid().ToString("N");
        var verifier = "verifier-a" + Guid.NewGuid().ToString("N");
        var start = await StartSessionAsync(client, state, verifier, 55101);

        var userA = await WebRegisterAsync(client, "desktop-a@vortex.local");
        Assert.Equal(VortexRoles.User, userA.User.Role);
        Assert.Contains("Ücretsiz", userA.User.PlanName);

        using var authedA = factory.CreateClient();
        authedA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userA.AccessToken);
        var statusA = await authedA.GetFromJsonAsync<AgentStatusDto>("/api/agent/status", JsonOptions);
        Assert.NotNull(statusA?.Profile);

        var completed = await CompleteAsync(authedA, start.SessionId, state);
        var code = GetQuery(completed.CallbackUrl, "code")!;
        Assert.Equal(state, GetQuery(completed.CallbackUrl, "state"));

        var wrongVerifier = await client.PostAsJsonAsync("/api/desktop-auth/token", new ExchangeDesktopCodeRequest(start.SessionId, code, "wrong-verifier", state), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, wrongVerifier.StatusCode);

        var token = await ExchangeAsync(client, start.SessionId, code, verifier, state);
        Assert.Equal(userA.User.Id, token.User.Id);

        var replay = await client.PostAsJsonAsync("/api/desktop-auth/token", new ExchangeDesktopCodeRequest(start.SessionId, code, verifier, state), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        using var desktop = factory.CreateClient();
        desktop.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var me = await desktop.GetFromJsonAsync<UserProfileDto>("/api/me", JsonOptions);
        Assert.Equal(userA.User.Id, me?.Id);

        var expiredState = "expired" + Guid.NewGuid().ToString("N");
        var expiredVerifier = "expired-verifier" + Guid.NewGuid().ToString("N");
        var expiredStart = await StartSessionAsync(client, expiredState, expiredVerifier, 55102);
        var expiredComplete = await CompleteAsync(authedA, expiredStart.SessionId, expiredState);
        await ExpireSessionAsync(factory.DataDirectory, expiredStart.SessionId);
        var expiredCode = GetQuery(expiredComplete.CallbackUrl, "code")!;
        var expiredExchange = await client.PostAsJsonAsync("/api/desktop-auth/token", new ExchangeDesktopCodeRequest(expiredStart.SessionId, expiredCode, expiredVerifier, expiredState), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, expiredExchange.StatusCode);

        var userB = await WebRegisterAsync(client, "desktop-b@vortex.local");
        using var authedB = factory.CreateClient();
        authedB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userB.AccessToken);
        var stateA2 = "a2" + Guid.NewGuid().ToString("N");
        var verifierA2 = "verifier-a2" + Guid.NewGuid().ToString("N");
        var stateB2 = "b2" + Guid.NewGuid().ToString("N");
        var verifierB2 = "verifier-b2" + Guid.NewGuid().ToString("N");
        var sessionA2 = await StartSessionAsync(client, stateA2, verifierA2, 55103);
        var sessionB2 = await StartSessionAsync(client, stateB2, verifierB2, 55104);
        var completeA2 = await CompleteAsync(authedA, sessionA2.SessionId, stateA2);
        await CompleteAsync(authedB, sessionB2.SessionId, stateB2);
        var codeA2 = GetQuery(completeA2.CallbackUrl, "code")!;
        var crossUser = await client.PostAsJsonAsync("/api/desktop-auth/token", new ExchangeDesktopCodeRequest(sessionB2.SessionId, codeA2, verifierB2, stateB2), JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, crossUser.StatusCode);
    }

    private static async Task<StartDesktopAuthResponse> StartSessionAsync(HttpClient client, string state, string verifier, int port)
    {
        var response = await client.PostAsJsonAsync("/api/desktop-auth/sessions", new StartDesktopAuthRequest(Hash(state), Hash(verifier), $"http://127.0.0.1:{port}/callback/"), JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StartDesktopAuthResponse>(JsonOptions))!;
    }

    private static async Task<AuthResponse> WebRegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/web/auth/register", new WebRegisterRequest(email, "ChangeMe123!", email, true), JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!;
    }

    private static async Task<CompleteDesktopAuthResponse> CompleteAsync(HttpClient authedClient, Guid sessionId, string state)
    {
        var response = await authedClient.PostAsJsonAsync($"/api/desktop-auth/sessions/{sessionId}/complete", new CompleteDesktopAuthRequest(sessionId, state), JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CompleteDesktopAuthResponse>(JsonOptions))!;
    }

    private static async Task<AuthResponse> ExchangeAsync(HttpClient client, Guid sessionId, string code, string verifier, string state)
    {
        var response = await client.PostAsJsonAsync("/api/desktop-auth/token", new ExchangeDesktopCodeRequest(sessionId, code, verifier, state), JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!;
    }

    private static async Task ExpireSessionAsync(string dataDirectory, Guid sessionId)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataDirectory, "vortex.db") }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE DesktopAuthSessions SET ExpiresAt = $expires WHERE Id = $id";
        command.Parameters.AddWithValue("$expires", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
        command.Parameters.AddWithValue("$id", sessionId.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private static string Hash(string value) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? GetQuery(string uri, string key)
    {
        var parsed = new Uri(uri);
        foreach (var part in parsed.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.Split('=', 2);
            if (split.Length == 2 && Uri.UnescapeDataString(split[0]) == key) return Uri.UnescapeDataString(split[1]);
        }
        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal sealed class DesktopAuthServerFactory : WebApplicationFactory<Program>
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "vortex-desktop-auth-tests", Guid.NewGuid().ToString("N"));
    public string DataDirectory => Path.Combine(_dataRoot, "db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vortex:DataDirectory"] = DataDirectory,
                ["Vortex:WebBaseUrl"] = "http://127.0.0.1:5080",
                ["Hermes:ProfilesRoot"] = Path.Combine(_dataRoot, "hermes-profiles"),
                ["Hermes:UseFakeGateway"] = "true",
                ["Jwt:SigningKey"] = "desktop-auth-test-signing-key-32-chars-minimum"
            });
        });
    }
}
