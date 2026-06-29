using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vortex.Shared;

namespace Vortex.Web.Pages;

public sealed class LoginModel(IHttpClientFactory httpClientFactory) : PageModel
{
    [BindProperty] public string Email { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    [BindProperty] public bool RememberMe { get; set; }
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet() => ReturnUrl ??= "/";

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ReturnUrl ??= "/";
        var client = httpClientFactory.CreateClient("vortex-server");
        var response = await client.PostAsJsonAsync("/api/web/auth/login", new WebLoginRequest(Email, Password, RememberMe), WebAuth.JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ErrorMessage = "E-posta veya parola hatalı.";
            return Page();
        }
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(WebAuth.JsonOptions, cancellationToken);
        if (auth is null)
        {
            ErrorMessage = "Giriş yanıtı okunamadı.";
            return Page();
        }
        WebAuth.SetTokenCookie(Response, auth, RememberMe);
        return LocalRedirect(ReturnUrl);
    }
}
