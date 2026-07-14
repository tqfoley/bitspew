using System.Text;
using NBitcoin.Crypto;

namespace Bitspew.Core;

/// <summary>
/// The canonical text a poster signs in their wallet, and the content-hash id derived from it.
/// The payload embeds the thread id, address, and timestamp so a signature cannot be replayed
/// into another thread, under another identity, or at another time.
/// </summary>
public static class PostIdentity
{
    /// <summary>Payload signed to create a new thread.</summary>
    public static string NewThreadPayload(string title, string address, long unixTimeSeconds, string body) =>
        $"bitspew v1\nnew-thread\ntitle: {title}\naddress: {address}\ntime: {unixTimeSeconds}\n\n{body}";

    /// <summary>Payload signed to reply to an existing thread.</summary>
    public static string ReplyPayload(string threadId, string address, long unixTimeSeconds, string body) =>
        $"bitspew v1\nreply\nthread: {threadId}\naddress: {address}\ntime: {unixTimeSeconds}\n\n{body}";

    /// <summary>
    /// Content-hash id of a post: double-SHA256 over the signed payload and the signature,
    /// hex-encoded. Recomputable by anyone, so ids are self-verifying and tamper-evident.
    /// </summary>
    public static string ComputeId(string payload, string signatureBase64) =>
        Hashes.DoubleSHA256(Encoding.UTF8.GetBytes(payload + "\n" + signatureBase64)).ToString();

    /// <summary>
    /// Normalizes user-entered text so the payload the server verifies matches what the wallet
    /// signed regardless of platform line endings.
    /// </summary>
    public static string NormalizeText(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
}
