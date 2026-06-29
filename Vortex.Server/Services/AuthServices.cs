using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Vortex.Server.Data;
using Vortex.Shared;

namespace Vortex.Server.Services;

public sealed class TokenService(IConfiguration configuration)
{
    private readonly string _issuer = configuration["Jwt:Issuer"] ?? "Vortex.Server";
    private readonly string _audience = configuration["Jwt:Audience"] ?? "Vortex.Clients";
    private readonly byte[] _key = Encoding.UTF8.GetBytes(configuration["Jwt:SigningKey"] ?? "CHANGE_ME_DEVELOPMENT_SIGNING_KEY_32_CHARS_MINIMUM");

    public string CreateToken(UserProfileDto user, DateTimeOffset expiresAt)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["iss"] = _issuer, ["aud"] = _audience, ["sub"] = user.Id.ToString(), ["email"] = user.Email,
            ["name"] = user.DisplayName, ["role"] = user.Role, ["exp"] = expiresAt.ToUnixTimeSeconds(), ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }));
        var unsigned = $"{header}.{payload}";
        using var hmac = new HMACSHA256(_key);
        return $"{unsigned}.{Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)))}";
    }

    public ClaimsPrincipal? ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        using var hmac = new HMACSHA256(_key);
        var expected = Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}")));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[2]))) return null;
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Base64UrlDecode(parts[1]));
        if (payload is null || !payload.TryGetValue("exp", out var exp) || DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()) < DateTimeOffset.UtcNow) return null;
        return new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, payload["sub"].GetString()!),
            new Claim(ClaimTypes.Email, payload["email"].GetString() ?? string.Empty),
            new Claim(ClaimTypes.Name, payload["name"].GetString() ?? string.Empty),
            new Claim(ClaimTypes.Role, payload["role"].GetString() ?? VortexRoles.User)
        }, "VortexJwt"));
    }

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}

public sealed class AuthService(VortexDb db, TokenService tokens, IHermesProfileService hermesProfiles)
{
    public Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
        => RegisterInternalAsync(request.Email, request.Password, request.DisplayName, allowFirstOwner: true, cancellationToken);

    public Task<AuthResponse> RegisterWebUserAsync(WebRegisterRequest request, CancellationToken cancellationToken)
    {
        if (!request.AcceptTerms) throw new InvalidOperationException("Kullanım koşulları onayı gereklidir.");
        return RegisterInternalAsync(request.Email, request.Password, request.DisplayName, allowFirstOwner: false, cancellationToken);
    }

    private async Task<AuthResponse> RegisterInternalAsync(string email, string password, string displayName, bool allowFirstOwner, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || password.Length < 8) throw new InvalidOperationException("E-posta ve en az 8 karakter parola gereklidir.");
        await using var connection = await db.OpenAsync(cancellationToken);
        var existingUsers = await VortexDb.ScalarLongAsync(connection, "SELECT COUNT(*) FROM Users", cancellationToken);
        var planId = await GetDefaultPlanIdAsync(connection, cancellationToken);
        var role = allowFirstOwner && existingUsers == 0 ? VortexRoles.Owner : VortexRoles.User;
        var id = Guid.NewGuid();
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var hash = VortexDb.HashSecret(password, salt);
        await VortexDb.ExecuteAsync(connection, "INSERT INTO Users (Id, Email, DisplayName, PasswordHash, PasswordSalt, Role, PlanId, StorageUsedBytes, CreatedAt) VALUES ($id, $email, $displayName, $hash, $salt, $role, $planId, 0, $createdAt)", cancellationToken,
            ("$id", id.ToString()), ("$email", email.Trim().ToLowerInvariant()), ("$displayName", displayName.Trim()), ("$hash", hash), ("$salt", salt), ("$role", role), ("$planId", planId), ("$createdAt", DateTimeOffset.UtcNow.ToString("O")));
        await hermesProfiles.EnsureProfileAsync(id, cancellationToken);
        return CreateAuthResponse(await GetProfileAsync(id, cancellationToken) ?? throw new InvalidOperationException("Kullanıcı oluşturulamadı."));
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, PasswordHash, PasswordSalt FROM Users WHERE Email = $email";
        command.Parameters.AddWithValue("$email", request.Email.Trim().ToLowerInvariant());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("E-posta veya parola hatalı.");
        var id = Guid.Parse(reader.GetString(0));
        var expected = reader.GetString(1);
        var actual = VortexDb.HashSecret(request.Password, reader.GetString(2));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual))) throw new InvalidOperationException("E-posta veya parola hatalı.");
        return CreateAuthResponse(await GetProfileAsync(id, cancellationToken) ?? throw new InvalidOperationException("Kullanıcı bulunamadı."));
    }

    public async Task<UserProfileDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT u.Id, u.Email, u.DisplayName, u.Role, p.DisplayName, p.StorageQuotaBytes, u.StorageUsedBytes FROM Users u JOIN SubscriptionPlans p ON p.Id = u.PlanId WHERE u.Id = $id";
        command.Parameters.AddWithValue("$id", userId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new UserProfileDto(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5), reader.GetInt64(6))
            : null;
    }

    private AuthResponse CreateAuthResponse(UserProfileDto profile)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(12);
        return new AuthResponse(tokens.CreateToken(profile, expiresAt), expiresAt, profile);
    }

    private static async Task<string> GetDefaultPlanIdAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM SubscriptionPlans WHERE Name = 'free' LIMIT 1";
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? throw new InvalidOperationException("Varsayılan plan bulunamadı.");
    }
}
