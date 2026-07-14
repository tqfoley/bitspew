using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Bitspew.Web.Pages;

/// <summary>
/// Intentionally empty: signing happens entirely in the browser (wwwroot/js/bitcoin-sign.js).
/// This page has no POST handler, so the private key is never sent to the server.
/// </summary>
public class SignMessageModel : PageModel
{
    public void OnGet()
    {
    }
}
