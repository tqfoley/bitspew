using Bitspew.Core;
using Bitspew.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Bitspew.Web.Pages.Threads;

public record PostView(Post Post, bool Verified);

public class ViewThreadModel(BitspewDbContext db, PostSubmissionService submissions, TimeProvider clock) : PageModel
{
    [BindProperty] public string Address { get; set; } = "";
    [BindProperty] public string Body { get; set; } = "";
    [BindProperty] public long SignedAtUnixSeconds { get; set; }
    [BindProperty] public string Signature { get; set; } = "";

    public MessageThread? Thread { get; private set; }
    public IReadOnlyList<PostView> Posts { get; private set; } = [];
    public string? PayloadToSign { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        return await LoadAsync(id) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostPrepareAsync(string id)
    {
        if (!await LoadAsync(id))
            return NotFound();

        Address = Address.Trim();
        Body = PostIdentity.NormalizeText(Body);
        if (Address.Length == 0 || Body.Length == 0)
        {
            ErrorMessage = "Address and message are both required.";
            return Page();
        }

        SignedAtUnixSeconds = clock.GetUtcNow().ToUnixTimeSeconds();
        PayloadToSign = PostIdentity.ReplyPayload(id, Address, SignedAtUnixSeconds, Body);
        return Page();
    }

    public async Task<IActionResult> OnPostPublishAsync(string id)
    {
        var result = await submissions.ReplyAsync(id, Address.Trim(), SignedAtUnixSeconds, Body, Signature.Trim());
        if (result.Success)
            return RedirectToPage("/Threads/View", new { id });

        if (!await LoadAsync(id))
            return NotFound();
        ErrorMessage = result.Error;
        PayloadToSign = PostIdentity.ReplyPayload(id, Address.Trim(), SignedAtUnixSeconds, PostIdentity.NormalizeText(Body));
        return Page();
    }

    private async Task<bool> LoadAsync(string id)
    {
        Thread = await db.Threads
            .Include(t => t.Posts.OrderByDescending(p => p.IsRoot).ThenBy(p => p.ReceivedAt))
            .FirstOrDefaultAsync(t => t.Id == id);
        if (Thread is null)
            return false;

        Posts = Thread.Posts.Select(post =>
        {
            var payload = post.IsRoot
                ? PostIdentity.NewThreadPayload(Thread.Title, post.Address, post.SignedAtUnixSeconds, post.Body)
                : PostIdentity.ReplyPayload(Thread.Id, post.Address, post.SignedAtUnixSeconds, post.Body);
            var verified = MessageSignatureVerifier.Verify(payload, post.SignatureBase64, post.Address);
            return new PostView(post, verified.IsSignedByAddress);
        }).ToList();
        return true;
    }
}
