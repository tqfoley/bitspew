using Bitspew.Core;
using Bitspew.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Bitspew.Web.Pages.Threads;

public class NewModel(PostSubmissionService submissions, TimeProvider clock) : PageModel
{
    [BindProperty] public string Title { get; set; } = "";
    [BindProperty] public string Address { get; set; } = "";
    [BindProperty] public string Body { get; set; } = "";
    [BindProperty] public long SignedAtUnixSeconds { get; set; }
    [BindProperty] public string Signature { get; set; } = "";

    public string? PayloadToSign { get; private set; }
    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
    }

    public void OnPostPrepare()
    {
        Title = PostIdentity.NormalizeText(Title);
        Address = Address.Trim();
        Body = PostIdentity.NormalizeText(Body);

        if (Title.Length == 0 || Address.Length == 0 || Body.Length == 0)
        {
            ErrorMessage = "Title, address, and body are all required.";
            return;
        }

        SignedAtUnixSeconds = clock.GetUtcNow().ToUnixTimeSeconds();
        PayloadToSign = PostIdentity.NewThreadPayload(Title, Address, SignedAtUnixSeconds, Body);
    }

    public async Task<IActionResult> OnPostPublishAsync()
    {
        var result = await submissions.CreateThreadAsync(
            Title, Address.Trim(), SignedAtUnixSeconds, Body, Signature.Trim());

        if (!result.Success)
        {
            ErrorMessage = result.Error;
            PayloadToSign = PostIdentity.NewThreadPayload(
                PostIdentity.NormalizeText(Title), Address.Trim(), SignedAtUnixSeconds, PostIdentity.NormalizeText(Body));
            return Page();
        }

        return RedirectToPage("/Threads/View", new { id = result.ThreadId });
    }
}
