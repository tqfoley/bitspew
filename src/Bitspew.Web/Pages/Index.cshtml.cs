using Bitspew.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Bitspew.Web.Pages;

public record ThreadListItem(string Id, string Title, string Address, int PostCount, DateTimeOffset LastActivity);

public class IndexModel(BitspewDbContext db) : PageModel
{
    public IReadOnlyList<ThreadListItem> Threads { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Threads = await db.Threads
            .OrderByDescending(t => t.Posts.Max(p => p.ReceivedAt))
            .Take(50)
            .Select(t => new ThreadListItem(
                t.Id,
                t.Title,
                t.Address,
                t.Posts.Count,
                t.Posts.Max(p => p.ReceivedAt)))
            .ToListAsync();
    }
}
