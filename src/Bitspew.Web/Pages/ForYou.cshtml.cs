using Bitspew.Core;
using Bitspew.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Bitspew.Web.Pages;

public class ForYouModel(BitspewDbContext db, TimeProvider clock) : PageModel
{
    [BindProperty] public string Body { get; set; } = "";

    public IReadOnlyList<Message> Messages { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync()
    {
        Messages = await LoadMessagesAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var body = PostIdentity.NormalizeText(Body);
        if (body.Length is 0 or > 10_000)
        {
            ErrorMessage = "Message must be between 1 and 10,000 characters.";
            Messages = await LoadMessagesAsync();
            return Page();
        }

        db.Messages.Add(new Message { Body = body, CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync();

        // Redirect-after-post so a refresh doesn't resubmit the message.
        return RedirectToPage();
    }

    private async Task<IReadOnlyList<Message>> LoadMessagesAsync() =>
        await db.Messages
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .ToListAsync();
}
