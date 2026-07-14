using NBitcoin;

namespace Bitspew.Core.Tests;

public class PostIdentityTests
{
    [Fact]
    public void SignedPayload_RoundTrips_ThroughVerifier()
    {
        var key = new Key();
        var address = key.PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main).ToString();
        var payload = PostIdentity.NewThreadPayload("Hello bitspew", address, 1_752_000_000, "First post!");
        var signature = MessageSigner.SignMessage(key, payload);

        var result = MessageSignatureVerifier.Verify(payload, signature, address);

        Assert.True(result.IsSignedByAddress);
    }

    [Fact]
    public void ComputeId_IsDeterministic64CharHex()
    {
        var id = PostIdentity.ComputeId("payload", "signature");

        Assert.Equal(64, id.Length);
        Assert.Matches("^[0-9a-f]{64}$", id);
        Assert.Equal(id, PostIdentity.ComputeId("payload", "signature"));
    }

    [Fact]
    public void ComputeId_ChangesWhenPayloadOrSignatureChanges()
    {
        var baseline = PostIdentity.ComputeId("payload", "signature");

        Assert.NotEqual(baseline, PostIdentity.ComputeId("payload!", "signature"));
        Assert.NotEqual(baseline, PostIdentity.ComputeId("payload", "signature!"));
    }

    [Fact]
    public void NormalizeText_UnifiesLineEndingsAndTrims()
    {
        Assert.Equal("a\nb\nc", PostIdentity.NormalizeText("  a\r\nb\rc\n\n"));
    }

    [Fact]
    public void ReplyPayload_BindsThreadAddressAndTime()
    {
        var payload = PostIdentity.ReplyPayload("abc123", "1SomeAddress", 1_752_000_000, "reply body");

        Assert.Contains("thread: abc123", payload);
        Assert.Contains("address: 1SomeAddress", payload);
        Assert.Contains("time: 1752000000", payload);
        Assert.EndsWith("\n\nreply body", payload);
    }
}
