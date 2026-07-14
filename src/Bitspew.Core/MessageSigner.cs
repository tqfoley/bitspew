using NBitcoin;

namespace Bitspew.Core;

/// <summary>
/// Produces "Bitcoin Signed Message" signatures compatible with wallet signmessage /
/// verifymessage. Useful for tests and tooling; end users normally sign in their own wallet.
/// </summary>
public static class MessageSigner
{
    public static string SignMessage(Key key, string message)
    {
        // The signature is identical either way; only the header byte records compression.
        // NBitcoin refuses SignCompact on uncompressed keys, so sign via a compressed twin.
        var compressed = key.PubKey.IsCompressed;
        var signingKey = compressed ? key : new Key(key.ToBytes());
        var signature = signingKey.SignCompact(BitcoinSignedMessage.ComputeHash(message));
        var header = (byte)(27 + signature.RecoveryId + (compressed ? 4 : 0));
        return Convert.ToBase64String([header, .. signature.Signature]);
    }
}
