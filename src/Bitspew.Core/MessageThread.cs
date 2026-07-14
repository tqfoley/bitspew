namespace Bitspew.Core;

/// <summary>
/// A discussion thread. Its id is the content hash of the root post, so the thread's identity
/// is self-verifying in the same way a Bitcoin txid is.
/// </summary>
public class MessageThread
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Address { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<Post> Posts { get; set; } = [];
}
