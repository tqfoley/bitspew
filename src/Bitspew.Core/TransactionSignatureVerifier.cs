using NBitcoin;

namespace Bitspew.Core;

/// <summary>
/// Verifies that a raw Bitcoin transaction is signed by the key controlling a given address.
/// Supports P2PKH (legacy "1...") and P2WPKH (bech32 "bc1q...") inputs.
/// </summary>
public static class TransactionSignatureVerifier
{
    /// <param name="transactionHex">The raw transaction, hex-encoded.</param>
    /// <param name="address">The address whose signatures should be verified.</param>
    /// <param name="network">Defaults to Bitcoin mainnet.</param>
    /// <param name="spentAmounts">
    /// Value of the output each input spends, keyed by input index. Required only for SegWit
    /// inputs: BIP143 signatures commit to the spent amount, which a raw transaction does not
    /// carry. Legacy P2PKH inputs verify without it.
    /// </param>
    public static TransactionVerificationResult Verify(
        string transactionHex,
        string address,
        Network? network = null,
        IReadOnlyDictionary<int, Money>? spentAmounts = null)
    {
        network ??= Network.Main;

        Transaction transaction;
        try
        {
            transaction = Transaction.Parse(transactionHex, network);
        }
        catch (Exception ex)
        {
            return TransactionVerificationResult.Failed($"Invalid transaction hex: {ex.Message}");
        }

        BitcoinAddress parsedAddress;
        try
        {
            parsedAddress = BitcoinAddress.Create(address, network);
        }
        catch (Exception ex)
        {
            return TransactionVerificationResult.Failed($"Invalid {network.Name} address: {ex.Message}");
        }

        var inputs = transaction.Inputs.AsIndexedInputs()
            .Select(input => VerifyInput(input, parsedAddress, spentAmounts))
            .ToList();

        var fromAddress = inputs
            .Where(r => r.Status is not (InputSignatureStatus.NotFromAddress or InputSignatureStatus.Unsupported))
            .ToList();
        var isSigned = fromAddress.Count > 0
            && fromAddress.All(r => r.Status == InputSignatureStatus.SignedByAddress);

        return new TransactionVerificationResult(isSigned, inputs);
    }

    private static InputVerificationResult VerifyInput(
        IndexedTxIn input,
        BitcoinAddress address,
        IReadOnlyDictionary<int, Money>? spentAmounts)
    {
        var index = (int)input.Index;
        var txIn = input.TxIn;

        if (txIn.WitScript is not null && txIn.WitScript.PushCount > 0)
        {
            var witness = PayToWitPubKeyHashTemplate.Instance.ExtractWitScriptParameters(txIn.WitScript);
            if (witness?.PublicKey is null)
                return new(index, InputSignatureStatus.Unsupported,
                    "Witness input is not P2WPKH (P2WSH and taproot are not supported).");

            if (address is not BitcoinWitPubKeyAddress witAddress
                || witness.PublicKey.WitHash != witAddress.Hash)
                return new(index, InputSignatureStatus.NotFromAddress);

            if (spentAmounts is null || !spentAmounts.TryGetValue(index, out var amount))
                return new(index, InputSignatureStatus.MissingSpentAmount,
                    "SegWit signatures commit to the spent amount (BIP143); supply the value of the output this input spends.");

            return RunScript(input, new TxOut(amount, address.ScriptPubKey));
        }

        var scriptSig = PayToPubkeyHashTemplate.Instance.ExtractScriptSigParameters(txIn.ScriptSig);
        if (scriptSig?.PublicKey is null)
            return new(index, InputSignatureStatus.Unsupported,
                "Input is not a signed P2PKH or P2WPKH spend.");

        if (address is not BitcoinPubKeyAddress pkhAddress
            || scriptSig.PublicKey.Hash != pkhAddress.Hash)
            return new(index, InputSignatureStatus.NotFromAddress);

        // Legacy sighash does not include the spent amount, so a placeholder value suffices.
        return RunScript(input, new TxOut(Money.Zero, address.ScriptPubKey));
    }

    private static InputVerificationResult RunScript(IndexedTxIn input, TxOut spentOutput)
    {
        var coin = new Coin(input.TxIn.PrevOut, spentOutput);
        return input.VerifyScript(coin, out var error)
            ? new((int)input.Index, InputSignatureStatus.SignedByAddress)
            : new((int)input.Index, InputSignatureStatus.InvalidSignature, $"Script verification failed: {error}.");
    }
}
