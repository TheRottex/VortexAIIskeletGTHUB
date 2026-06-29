using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Vortex.Server.Data;
using Vortex.Shared;

namespace Vortex.Server.Services;

public sealed class DesktopAuthService(VortexDb db, AuthService authService, TokenService tokenService, IConfiguration configuration)
{
    public async Task<StartDesktopAuthResponse> StartAsync(StartDesktopAuthRequest request, CancellationToken cancellationToken)
    {
        if (!IsLoopbackCallback(request.CallbackUri)) throw new InvalidOperationException("Callback adresi yalnızca loopback olabilir.");
        if (string.IsNullOrWhiteSpace(request.StateHash) || string.IsNullOrWhiteSpace(request.CodeChallenge)) throw new InvalidOperationException("State ve PKCE bilgisi gereklidir.");
        var id = Guid.NewGuid();
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "INSERT INTO DesktopAuthSessions (Id, StateHash, CodeChallenge, CallbackUri, UserId, AuthorizationCodeHash, ExpiresAt, CompletedAt, ConsumedAt, CreatedAt) VALUES ($id, $state, $challenge, $callback, NULL, NULL, $expires, NULL, NULL, $created)", cancellationToken,
            ("$id", id.ToString()), ("$state", request.StateHash), ("$challenge", request.CodeChallenge), ("$callback", request.CallbackUri), ("$expires", expires.ToString("O")), ("$created", DateTimeOffset.UtcNow.ToString("O")));
        var webBase = configuration["Vortex:WebBaseUrl"] ?? "http://127.0.0.1:5080";
        return new StartDesktopAuthResponse(id, $"{webBase.TrimEnd('/')}/desktop/authorize?sessionId={id}", expires);
    }

    public async Task<CompleteDesktopAuthResponse> CompleteAsync(Guid sessionId, Guid userId, string state, CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken) ?? throw new InvalidOperationException("Oturum bulunamadı.");
        if (session.ExpiresAt < DateTimeOffset.UtcNow) throw new InvalidOperationException("Oturum süresi doldu.");
        if (session.ConsumedAt is not null) throw new InvalidOperationException("Oturum daha önce kullanılmış.");
        if (string.IsNullOrWhiteSpace(state) || !FixedEquals(session.StateHash, Hash(state))) throw new InvalidOperationException("State doğrulaması başarısız.");
        var code = TokenService.Base64Url(RandomNumberGenerator.GetBytes(32));
        var codeHash = Hash(code);
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "UPDATE DesktopAuthSessions SET UserId = $userId, AuthorizationCodeHash = $codeHash, CompletedAt = $completedAt WHERE Id = $id AND ConsumedAt IS NULL", cancellationToken,
            ("$userId", userId.ToString()), ("$codeHash", codeHash), ("$completedAt", DateTimeOffset.UtcNow.ToString("O")), ("$id", sessionId.ToString()));
        var callback = AppendQuery(session.CallbackUri, new Dictionary<string, string> { ["code"] = code, ["state"] = state });
        return new CompleteDesktopAuthResponse(callback, "Giriş başarılı. Artık işleminize Vortex uygulamasından devam edebilirsiniz.");
    }

    public async Task<AuthResponse> ExchangeAsync(ExchangeDesktopCodeRequest request, CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(request.SessionId, cancellationToken) ?? throw new InvalidOperationException("Oturum bulunamadı.");
        if (session.ExpiresAt < DateTimeOffset.UtcNow) throw new InvalidOperationException("Authorization code süresi doldu.");
        if (session.ConsumedAt is not null) throw new InvalidOperationException("Authorization code daha önce kullanıldı.");
        if (session.CompletedAt is null || session.UserId is null || string.IsNullOrWhiteSpace(session.AuthorizationCodeHash)) throw new InvalidOperationException("Oturum tamamlanmamış.");
        if (!FixedEquals(session.StateHash, Hash(request.State))) throw new InvalidOperationException("State doğrulaması başarısız.");
        if (!FixedEquals(session.CodeChallenge, CreateChallenge(request.CodeVerifier))) throw new InvalidOperationException("PKCE doğrulaması başarısız.");
        if (!FixedEquals(session.AuthorizationCodeHash, Hash(request.Code))) throw new InvalidOperationException("Authorization code geçersiz.");
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "UPDATE DesktopAuthSessions SET ConsumedAt = $consumedAt WHERE Id = $id AND ConsumedAt IS NULL", cancellationToken, ("$consumedAt", DateTimeOffset.UtcNow.ToString("O")), ("$id", request.SessionId.ToString()));
        var profile = await authService.GetProfileAsync(session.UserId.Value, cancellationToken) ?? throw new InvalidOperationException("Kullanıcı bulunamadı.");
        var expires = DateTimeOffset.UtcNow.AddHours(12);
        return new AuthResponse(tokenService.CreateToken(profile, expires), expires, profile);
    }

    public async Task<DesktopAuthStatusResponse> GetStatusAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken) ?? throw new InvalidOperationException("Oturum bulunamadı.");
        return new DesktopAuthStatusResponse(sessionId, session.CompletedAt is not null, session.ConsumedAt is not null, session.ExpiresAt);
    }

    public async Task CancelAsync(Guid sessionId, string state, CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken) ?? throw new InvalidOperationException("Oturum bulunamadı.");
        if (!FixedEquals(session.StateHash, Hash(state))) throw new InvalidOperationException("State doğrulaması başarısız.");
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "UPDATE DesktopAuthSessions SET ConsumedAt = $now WHERE Id = $id AND ConsumedAt IS NULL", cancellationToken, ("$now", DateTimeOffset.UtcNow.ToString("O")), ("$id", sessionId.ToString()));
    }

    private async Task<DesktopSession?> GetSessionAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, StateHash, CodeChallenge, CallbackUri, UserId, AuthorizationCodeHash, ExpiresAt, CompletedAt, ConsumedAt FROM DesktopAuthSessions WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var callback = reader.GetString(3);
        return new DesktopSession(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), callback, reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)), reader.IsDBNull(5) ? null : reader.GetString(5), DateTimeOffset.Parse(reader.GetString(6)), reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)), reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)));
    }

    public static string Hash(string value) => TokenService.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string CreateChallenge(string verifier) => Hash(verifier);

    private static bool FixedEquals(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static bool IsLoopbackCallback(string callbackUri)
        => Uri.TryCreate(callbackUri, UriKind.Absolute, out var uri) && (uri.Host == "127.0.0.1" || uri.Host == "localhost" || uri.Host == "[::1]") && (uri.Scheme == "http" || uri.Scheme == "https");

    private static string AppendQuery(string uri, Dictionary<string, string> values)
    {
        var separator = uri.Contains('?') ? "&" : "?";
        return uri + separator + string.Join('&', values.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    private sealed record DesktopSession(Guid Id, string StateHash, string CodeChallenge, string CallbackUri, Guid? UserId, string? AuthorizationCodeHash, DateTimeOffset ExpiresAt, DateTimeOffset? CompletedAt, DateTimeOffset? ConsumedAt);
}
