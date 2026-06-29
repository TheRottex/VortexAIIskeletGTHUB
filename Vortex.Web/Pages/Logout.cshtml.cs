using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Vortex.Web.Pages;

public sealed class LogoutModel : PageModel
{
    public void OnGet()
    {
        Response.Cookies.Delete(WebAuth.TokenCookie);
    }
}
