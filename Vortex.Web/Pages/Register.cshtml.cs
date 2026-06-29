using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vortex.Shared;

namespace Vortex.Web.Pages;

public sealed class RegisterModel(IHttpClientFactory httpClientFactory) : PageModel
{
    [BindProperty] public string DisplayName { get; set; } = string.Empty;
    [BindProperty] public string Email { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    [BindProperty] public string ConfirmPassword { get; set; } = string.Empty;
    [BindProperty] public bool AcceptTerms { get; set; }
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet() => ReturnUrl ??= "/";

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ReturnUrl ??= "/";
        if (Password != ConfirmPassword)
        {
            ErrorMessage = "Parolalar eşleşmiyor.";
            return Page();
        }
        var client = httpClientFactory.CreateClient("vortex-server");
        var response = await client.PostAsJsonAsync("/api/web/auth/register", new WebRegisterRequest(Email, Password, DisplayName, AcceptTerms), WebAuth.JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ErrorMessage = "Kayıt tamamlanamadı. Bilgileri kontrol edin.";
            return Page();
        }
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(WebAuth.JsonOptions, cancellationToken);
        if (auth is null)
        {
            ErrorMessage = "Kayıt yanıtı okunamadı.";
            return Page();
        }
        WebAuth.SetTokenCookie(Response, auth, remember: false);
        return LocalRedirect(ReturnUrl);
    }
}
