using System.Text;
using NBitcoin;
using NBitcoin.Crypto;

namespace Bitspew.Core;

/// <summary>
/// Computes the digest used by the "Bitcoin Signed Message" standard: the magic prefix and the
/// message are each written as length-prefixed strings, then double-SHA256 hashed. The prefix
/// guarantees a signed message can never be mistaken for a transaction signature.
/// </summary>
public static class BitcoinSignedMessage
{
    private const string MagicPrefix = "Bitcoin Signed Message:\n";

    public static uint256 ComputeHash(string message)
    {
        using var buffer = new MemoryStream();
        var stream = new BitcoinStream(buffer, serializing: true);
        var prefix = Encoding.UTF8.GetBytes(MagicPrefix);
        var body = Encoding.UTF8.GetBytes(message);
        stream.ReadWriteAsVarString(ref prefix);
        stream.ReadWriteAsVarString(ref body);
        return Hashes.DoubleSHA256(buffer.ToArray());
    }
}
