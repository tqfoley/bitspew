namespace Bitspew.Core;

/// <summary>
/// A signed post. The id is the double-SHA256 of the canonical signing payload plus the
/// signature, so any reader can recompute it and detect tampering.
/// </summary>
public class Post
{
    public required string Id { get; set; }
    public required string ThreadId { get; set; }
    public MessageThread? Thread { get; set; }

    /// <summary>True for the post that created the thread.</summary>
    public bool IsRoot { get; set; }

    /// <summary>The Bitcoin address that signed this post.</summary>
    public required string Address { get; set; }

    public required string Body { get; set; }

    /// <summary>The 65-byte compact "Bitcoin Signed Message" signature, base64-encoded.</summary>
    public required string SignatureBase64 { get; set; }

    /// <summary>The unix timestamp embedded in the signed payload.</summary>
    public long SignedAtUnixSeconds { get; set; }

    /// <summary>When the server accepted the post.</summary>
    public DateTimeOffset ReceivedAt { get; set; }
}
