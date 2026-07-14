using Bitspew.Core;
using Microsoft.EntityFrameworkCore;

namespace Bitspew.Web.Data;

public sealed record SubmissionResult(bool Success, string? Error = null, string? ThreadId = null, string? PostId = null)
{
    public static SubmissionResult Failed(string error) => new(false, error);
}

/// <summary>Verifies signed submissions and stores them. Signatures are checked server-side
/// once at submission; they are stored so any reader can re-verify independently.</summary>
public class PostSubmissionService(BitspewDbContext db, TimeProvider clock)
{
    /// <summary>How far the signed timestamp may drift from server time before we reject it,
    /// bounding how long a captured payload+signature can be replayed.</summary>
    public static readonly TimeSpan TimestampTolerance = TimeSpan.FromMinutes(15);

    public async Task<SubmissionResult> CreateThreadAsync(
        string title, string address, long signedAtUnixSeconds, string body, string signatureBase64)
    {
        title = PostIdentity.NormalizeText(title);
        if (title.Length is 0 or > 200 || title.Contains('\n'))
            return SubmissionResult.Failed("Title must be a single line of at most 200 characters.");

        var payload = PostIdentity.NewThreadPayload(title, address, signedAtUnixSeconds, PostIdentity.NormalizeText(body));
        return await SubmitAsync(payload, address, signedAtUnixSeconds, body, signatureBase64,
            threadId: null, title: title);
    }

    public async Task<SubmissionResult> ReplyAsync(
        string threadId, string address, long signedAtUnixSeconds, string body, string signatureBase64)
    {
        if (!await db.Threads.AnyAsync(t => t.Id == threadId))
            return SubmissionResult.Failed("Thread not found.");

        var payload = PostIdentity.ReplyPayload(threadId, address, signedAtUnixSeconds, PostIdentity.NormalizeText(body));
        return await SubmitAsync(payload, address, signedAtUnixSeconds, body, signatureBase64,
            threadId: threadId, title: null);
    }

    private async Task<SubmissionResult> SubmitAsync(
        string payload, string address, long signedAtUnixSeconds, string body, string signatureBase64,
        string? threadId, string? title)
    {
        body = PostIdentity.NormalizeText(body);
        if (body.Length is 0 or > 10_000)
            return SubmissionResult.Failed("Body must be between 1 and 10,000 characters.");

        var now = clock.GetUtcNow();
        var signedAt = DateTimeOffset.FromUnixTimeSeconds(signedAtUnixSeconds);
        if ((now - signedAt).Duration() > TimestampTolerance)
            return SubmissionResult.Failed("The signed timestamp has expired; generate a fresh message to sign.");

        var verification = MessageSignatureVerifier.Verify(payload, signatureBase64, address);
        if (verification.Error is not null)
            return SubmissionResult.Failed(verification.Error);
        if (!verification.IsSignedByAddress)
            return SubmissionResult.Failed("Signature does not match the address. Sign the message text exactly as shown.");

        var postId = PostIdentity.ComputeId(payload, signatureBase64);
        if (await db.Posts.AnyAsync(p => p.Id == postId))
            return SubmissionResult.Failed("This exact signed post already exists.");

        var isRoot = threadId is null;
        if (isRoot)
        {
            threadId = postId;
            db.Threads.Add(new MessageThread
            {
                Id = threadId,
                Title = title!,
                Address = address,
                CreatedAt = now,
            });
        }

        db.Posts.Add(new Post
        {
            Id = postId,
            ThreadId = threadId!,
            IsRoot = isRoot,
            Address = address,
            Body = body,
            SignatureBase64 = signatureBase64,
            SignedAtUnixSeconds = signedAtUnixSeconds,
            ReceivedAt = now,
        });

        await db.SaveChangesAsync();
        return new SubmissionResult(true, ThreadId: threadId, PostId: postId);
    }
}
