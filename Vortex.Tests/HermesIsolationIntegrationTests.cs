using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Vortex.Shared;

namespace Vortex.Tests;

public sealed class HermesIsolationIntegrationTests
{
    [Fact]
    public async Task RegisteredUsers_GetIsolatedHermesProfiles_AndFreeLimitsAreEnforced()
    {
        using var factory = new VortexServerFactory();
        using var client = factory.CreateClient();

        var userA = await RegisterAsync(client, "user-a@vortex.local");
        var userB = await RegisterAsync(client, "user-b@vortex.local");

        using var clientA = factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userA.AccessToken);
        using var clientB = factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userB.AccessToken);

        var statusA = await GetStatusAsync(clientA);
        var statusB = await GetStatusAsync(clientB);

        Assert.NotNull(statusA.Profile);
        Assert.NotNull(statusB.Profile);
        Assert.NotEqual(statusA.Profile!.HermesProfileName, statusB.Profile!.HermesProfileName);
        Assert.NotEqual(statusA.Profile.HermesHomePath, statusB.Profile.HermesHomePath);
        Assert.True(Directory.Exists(Path.Combine(statusA.Profile.HermesHomePath, "workspace")));
        Assert.True(Directory.Exists(Path.Combine(statusB.Profile.HermesHomePath, "workspace")));
        Assert.Equal(5, statusA.Policy.DailyAgentRunLimit);
        Assert.Equal(3, statusA.Policy.ActiveScheduledTaskLimit);
        Assert.Equal(25, statusA.Policy.PersistentMemoryLimit);
        Assert.False(statusA.Policy.IsSubAgentEnabled);
        Assert.False(statusA.Policy.IsTerminalEnabled);
        Assert.False(statusA.Policy.IsSystemCommandEnabled);
        Assert.Equal(60, statusA.Policy.MaxRunSeconds);
        Assert.Equal(1, statusA.Policy.MaxConcurrentRuns);

        var remember = await PostChatAsync(clientA, new AgentChatRequest("remember: user-a-private-memory", statusB.Profile.Id));
        Assert.Equal(statusA.Profile.HermesProfileName, remember.ProfileName);

        var recallB = await PostChatAsync(clientB, new AgentChatRequest("recall", statusA.Profile.Id));
        Assert.DoesNotContain("user-a-private-memory", recallB.Response);
        Assert.Equal(statusB.Profile.HermesProfileName, recallB.ProfileName);

        for (var i = 0; i < 4; i++)
        {
            var ok = await PostChatAsync(clientA, new AgentChatRequest($"run {i}", statusB.Profile.Id));
            Assert.Equal(statusA.Profile.HermesProfileName, ok.ProfileName);
        }

        var sixth = await clientA.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest("sixth should be rejected", statusB.Profile.Id));
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);

        var postLimitStatusA = await GetStatusAsync(clientA);
        var postLimitStatusB = await GetStatusAsync(clientB);
        Assert.Equal(0, postLimitStatusA.RemainingRunsToday);
        Assert.Equal(4, postLimitStatusB.RemainingRunsToday);

        for (var i = 0; i < 3; i++)
        {
            var taskResponse = await clientA.PostAsJsonAsync("/api/agent/tasks", new CreateAgentTaskRequest($"task-{i}", $"*/{i + 1} * * * *", "Europe/Istanbul"));
            Assert.Equal(HttpStatusCode.OK, taskResponse.StatusCode);
        }

        var fourthTask = await clientA.PostAsJsonAsync("/api/agent/tasks", new CreateAgentTaskRequest("task-4", "*/5 * * * *", "Europe/Istanbul"));
        Assert.Equal(HttpStatusCode.TooManyRequests, fourthTask.StatusCode);

        var tasksA = await clientA.GetFromJsonAsync<List<AgentTaskDto>>("/api/agent/tasks", JsonOptions) ?? [];
        var tasksB = await clientB.GetFromJsonAsync<List<AgentTaskDto>>("/api/agent/tasks", JsonOptions) ?? [];
        Assert.Equal(3, tasksA.Count);
        Assert.Empty(tasksB);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "ChangeMe123!", email));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!;
    }

    private static async Task<AgentStatusDto> GetStatusAsync(HttpClient client)
    {
        var status = await client.GetFromJsonAsync<AgentStatusDto>("/api/agent/status", JsonOptions);
        return status ?? throw new InvalidOperationException("Agent status boş döndü.");
    }

    private static async Task<AgentChatResponse> PostChatAsync(HttpClient client, AgentChatRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/agent/chat", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentChatResponse>(JsonOptions))!;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal sealed class VortexServerFactory : WebApplicationFactory<Program>
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "vortex-hermes-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vortex:DataDirectory"] = Path.Combine(_dataRoot, "db"),
                ["Hermes:ProfilesRoot"] = Path.Combine(_dataRoot, "hermes-profiles"),
                ["Hermes:UseFakeGateway"] = "true",
                ["Jwt:SigningKey"] = "integration-test-signing-key-32-chars-minimum"
            });
        });
    }
}
