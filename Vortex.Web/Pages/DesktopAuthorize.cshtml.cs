using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Vortex.Shared;

namespace Vortex.Web.Pages;

public sealed class DesktopAuthorizeModel(IHttpClientFactory httpClientFactory) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid SessionId { get; set; }
    [BindProperty(SupportsGet = true)] public string State { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        if (!Request.Cookies.ContainsKey(WebAuth.TokenCookie))
        {
            var returnUrl = $"/desktop/authorize?sessionId={SessionId}&state={Uri.EscapeDataString(State)}";
            return Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var client = WebAuth.CreateServerClient(httpClientFactory, Request);
        var response = await client.PostAsJsonAsync($"/api/desktop-auth/sessions/{SessionId}/complete", new CompleteDesktopAuthRequest(SessionId, State), WebAuth.JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ErrorMessage = "Desktop yetkilendirme tamamlanamadı. Oturum süresi dolmuş olabilir.";
            return Page();
        }
        var result = await response.Content.ReadFromJsonAsync<CompleteDesktopAuthResponse>(WebAuth.JsonOptions, cancellationToken);
        if (result is null)
        {
            ErrorMessage = "Yetkilendirme yanıtı okunamadı.";
            return Page();
        }
        return Redirect($"/desktop/success?callbackUrl={Uri.EscapeDataString(result.CallbackUrl)}");
    }
}
