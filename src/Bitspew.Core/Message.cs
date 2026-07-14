namespace Bitspew.Core;

/// <summary>
/// A plain, unsigned message on the "For you" feed. Unlike <see cref="Post"/>, no signature
/// is required; signing may be layered on later.
/// </summary>
public class Message
{
    public long Id { get; set; }

    public required string Body { get; set; }

    /// <summary>When the server accepted the message.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
