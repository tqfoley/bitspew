using NBitcoin;

namespace Bitspew.Core;

/// <summary>Result of verifying a signed message against an address.</summary>
public sealed record MessageVerificationResult(bool IsSignedByAddress, string? Error = null)
{
    public static MessageVerificationResult Failed(string error) => new(false, error);
}

/// <summary>
/// Verifies "Bitcoin Signed Message" signatures — the off-chain signmessage/verifymessage
/// format wallets like Bitcoin Core, Electrum, Trezor and Ledger produce. The message is
/// hashed with the magic prefix and the public key is recovered from the 65-byte compact
/// signature, so verification needs only the message, the signature, and the address.
/// Supports P2PKH ("1..."), P2WPKH ("bc1q...") and P2SH-P2WPKH ("3...") addresses, accepting
/// both legacy and BIP137 header bytes. P2WSH and taproot would require BIP322.
/// </summary>
public static class MessageSignatureVerifier
{
    /// <param name="message">The exact text that was signed.</param>
    /// <param name="signatureBase64">The 65-byte compact signature, base64-encoded.</param>
    /// <param name="address">The address claimed to have signed the message.</param>
    /// <param name="network">Defaults to Bitcoin mainnet.</param>
    public static MessageVerificationResult Verify(
        string message,
        string signatureBase64,
        string address,
        Network? network = null)
    {
        network ??= Network.Main;

        BitcoinAddress parsedAddress;
        try
        {
            parsedAddress = BitcoinAddress.Create(address, network);
        }
        catch (Exception ex)
        {
            return MessageVerificationResult.Failed($"Invalid {network.Name} address: {ex.Message}");
        }

        PubKey recovered;
        try
        {
            recovered = RecoverPublicKey(message, signatureBase64);
        }
        catch (Exception ex)
        {
            return MessageVerificationResult.Failed($"Invalid signature: {ex.Message}");
        }

        return parsedAddress switch
        {
            BitcoinPubKeyAddress p2pkh => new(recovered.Hash == p2pkh.Hash),
            BitcoinWitPubKeyAddress p2wpkh => new(recovered.WitHash == p2wpkh.Hash),
            BitcoinScriptAddress p2sh => new(recovered.WitHash.ScriptPubKey.Hash == p2sh.Hash),
            _ => MessageVerificationResult.Failed(
                $"Address type {parsedAddress.GetType().Name} is not supported; P2WSH and taproot message signing requires BIP322."),
        };
    }

    private static PubKey RecoverPublicKey(string message, string signatureBase64)
    {
        var signature = Convert.FromBase64String(signatureBase64.Trim());
        if (signature.Length != 65)
            throw new FormatException($"Expected a 65-byte compact signature, got {signature.Length} bytes.");

        // The header byte encodes the recovery id, key compression, and (per BIP137) the
        // signer's address type: 27-30 uncompressed P2PKH, 31-34 compressed P2PKH,
        // 35-38 P2SH-P2WPKH, 39-42 P2WPKH. Recovery needs only the id and compression.
        var header = signature[0];
        if (header is < 27 or > 42)
            throw new FormatException($"Invalid signature header byte {header}; expected 27-42.");

        var recovered = PubKey.RecoverCompact(
            BitcoinSignedMessage.ComputeHash(message),
            new CompactSignature((header - 27) % 4, signature[1..]));

        // RecoverCompact always yields the compressed encoding; uncompressed-key addresses
        // hash the 65-byte encoding of the same point.
        return header >= 31 ? recovered : Decompress(recovered);
    }

    private static PubKey Decompress(PubKey compressedKey)
    {
        var p = System.Numerics.BigInteger.Parse(
            "0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC2F",
            System.Globalization.NumberStyles.HexNumber);
        var bytes = compressedKey.ToBytes();
        var x = new System.Numerics.BigInteger(bytes.AsSpan(1), isUnsigned: true, isBigEndian: true);

        // Solve y^2 = x^3 + 7 over the secp256k1 field; p = 3 (mod 4) so the square root is
        // a single ModPow, and the header's parity bit picks between y and p - y.
        var y = System.Numerics.BigInteger.ModPow(
            (System.Numerics.BigInteger.ModPow(x, 3, p) + 7) % p, (p + 1) / 4, p);
        if (y.IsEven != (bytes[0] == 0x02))
            y = p - y;

        var uncompressed = new byte[65];
        uncompressed[0] = 0x04;
        WriteBigEndian(x, uncompressed.AsSpan(1, 32));
        WriteBigEndian(y, uncompressed.AsSpan(33, 32));
        return new PubKey(uncompressed);
    }

    private static void WriteBigEndian(System.Numerics.BigInteger value, Span<byte> destination)
    {
        var raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        raw.CopyTo(destination[(destination.Length - raw.Length)..]);
    }
}
