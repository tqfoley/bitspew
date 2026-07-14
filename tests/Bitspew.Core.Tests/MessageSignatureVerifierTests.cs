using NBitcoin;

namespace Bitspew.Core.Tests;

public class MessageSignatureVerifierTests
{
    private static readonly Network Net = Network.Main;
    private const string Message = "bitspew: bitcoin is pretty great!";

    [Fact]
    public void LegacyAddress_ValidSignature_Verifies()
    {
        var key = new Key();
        var address = key.PubKey.GetAddress(ScriptPubKeyType.Legacy, Net).ToString();
        var signature = MessageSigner.SignMessage(key, Message);

        var result = MessageSignatureVerifier.Verify(Message, signature, address);

        Assert.Null(result.Error);
        Assert.True(result.IsSignedByAddress);
    }

    [Fact]
    public void SegwitAddress_LegacyStyleSignature_Verifies()
    {
        // Electrum signs with bech32 addresses using the legacy header bytes.
        var key = new Key();
        var address = key.PubKey.GetAddress(ScriptPubKeyType.Segwit, Net).ToString();
        var signature = MessageSigner.SignMessage(key, Message);

        var result = MessageSignatureVerifier.Verify(Message, signature, address);

        Assert.Null(result.Error);
        Assert.True(result.IsSignedByAddress);
    }

    [Fact]
    public void SegwitAddress_Bip137HeaderByte_Verifies()
    {
        var key = new Key();
        var address = key.PubKey.GetAddress(ScriptPubKeyType.Segwit, Net).ToString();
        var signature = Convert.FromBase64String(MessageSigner.SignMessage(key, Message));
        signature[0] += 8; // shift compressed-key header (31-34) into the BIP137 P2WPKH range (39-42)

        var result = MessageSignatureVerifier.Verify(Message, Convert.ToBase64String(signature), address);

        Assert.Null(result.Error);
        Assert.True(result.IsSignedByAddress);
    }

    [Fact]
    public void NestedSegwitAddress_Verifies()
    {
        var key = new Key();
        var address = key.PubKey.GetAddress(ScriptPubKeyType.SegwitP2SH, Net).ToString();
        var signature = MessageSigner.SignMessage(key, Message);

        var result = MessageSignatureVerifier.Verify(Message, signature, address);

        Assert.Null(result.Error);
        Assert.True(result.IsSignedByAddress);
    }

    [Fact]
    public void UncompressedKey_LegacyAddress_Verifies()
    {
        var key = new Key(RandomUtils.GetBytes(32), fCompressedIn: false);
        var address = key.PubKey.GetAddress(ScriptPubKeyType.Legacy, Net).ToString();
        var signature = MessageSigner.SignMessage(key, Message);

        var result = MessageSignatureVerifier.Verify(Message, signature, address);

        Assert.Null(result.Error);
        Assert.True(result.IsSignedByAddress);
    }

    [Fact]
    public void KnownExternalVector_Verifies()
    {
        // Published test vector from bitcoinjs-message; proves interop with signatures
        // produced by other implementations, not just our own round-trip.
        var result = MessageSignatureVerifier.Verify(
            "This is an example of a signed message.",
            "H9L5yLFjti0QTHhPyFrZCT1V/MMnBtXKmoiKDZ78NDBjERki6ZTQZdSMCtkgoNmp17By9ItJr8o7ChX0XxY91nk=",
            "1F3sAm6ZtwLAUnj7d38pGFxtP3RVEvtsbV");

        Assert.Null(result.Error);
        Assert.True(result.IsSignedByAddress);
    }

    [Fact]
    public void TamperedMessage_DoesNotVerify()
    {
        var key = new Key();
        var address = key.PubKey.GetAddress(ScriptPubKeyType.Legacy, Net).ToString();
        var signature = MessageSigner.SignMessage(key, Message);

        var result = MessageSignatureVerifier.Verify(Message + " (edited)", signature, address);

        Assert.False(result.IsSignedByAddress);
    }

    [Fact]
    public void SignatureFromDifferentKey_DoesNotVerify()
    {
        var key = new Key();
        var address = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Net).ToString();
        var signature = MessageSigner.SignMessage(key, Message);

        var result = MessageSignatureVerifier.Verify(Message, signature, address);

        Assert.False(result.IsSignedByAddress);
    }

    [Fact]
    public void TaprootAddress_IsReportedAsUnsupported()
    {
        var key = new Key();
        var address = key.PubKey.GetAddress(ScriptPubKeyType.TaprootBIP86, Net).ToString();
        var signature = MessageSigner.SignMessage(key, Message);

        var result = MessageSignatureVerifier.Verify(Message, signature, address);

        Assert.False(result.IsSignedByAddress);
        Assert.Contains("BIP322", result.Error);
    }

    [Fact]
    public void MalformedBase64_ReturnsError()
    {
        var address = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Net).ToString();

        var result = MessageSignatureVerifier.Verify(Message, "not base64!!!", address);

        Assert.False(result.IsSignedByAddress);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void WrongSignatureLength_ReturnsError()
    {
        var address = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Net).ToString();
        var tooShort = Convert.ToBase64String(new byte[10]);

        var result = MessageSignatureVerifier.Verify(Message, tooShort, address);

        Assert.False(result.IsSignedByAddress);
        Assert.Contains("65-byte", result.Error);
    }

    [Fact]
    public void InvalidAddress_ReturnsError()
    {
        var key = new Key();
        var signature = MessageSigner.SignMessage(key, Message);

        var result = MessageSignatureVerifier.Verify(Message, signature, "definitely-not-an-address");

        Assert.False(result.IsSignedByAddress);
        Assert.NotNull(result.Error);
    }
}
