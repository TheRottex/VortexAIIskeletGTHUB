using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Vortex.Shared;

namespace Vortex.Web.Pages;

internal static class WebAuth
{
    public const string TokenCookie = "vortex_web_access_token";
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void SetTokenCookie(HttpResponse response, AuthResponse auth, bool remember)
    {
        response.Cookies.Append(TokenCookie, auth.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = false,
            Expires = remember ? DateTimeOffset.UtcNow.AddDays(14) : auth.ExpiresAt
        });
    }

    public static HttpClient CreateServerClient(IHttpClientFactory factory, HttpRequest request)
    {
        var client = factory.CreateClient("vortex-server");
        if (request.Cookies.TryGetValue(TokenCookie, out var token) && !string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }
}
