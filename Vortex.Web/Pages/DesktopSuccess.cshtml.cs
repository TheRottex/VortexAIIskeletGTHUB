using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Vortex.Web.Pages;

public sealed class DesktopSuccessModel : PageModel
{
    public string CallbackUrl { get; private set; } = "/";

    public void OnGet(string callbackUrl)
    {
        CallbackUrl = callbackUrl;
    }
}
