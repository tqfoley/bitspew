using NBitcoin;

namespace Bitspew.Core.Tests;

public class TransactionSignatureVerifierTests
{
    private static readonly Network Net = Network.Main;

    private static (Key Key, BitcoinAddress Address, Coin Coin) CreateFundedKey(ScriptPubKeyType type)
    {
        var key = new Key();
        var address = key.PubKey.GetAddress(type, Net);
        var prevTx = Net.CreateTransaction();
        prevTx.Outputs.Add(Money.Coins(1m), address.ScriptPubKey);
        return (key, address, new Coin(prevTx, 0));
    }

    private static string BuildSignedSpendHex(Key key, Coin coin)
    {
        var destination = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Net);
        var builder = Net.CreateTransactionBuilder();
        var tx = builder
            .AddCoins(coin)
            .AddKeys(key)
            .Send(destination, Money.Coins(0.9m))
            .SendFees(Money.Coins(0.1m))
            .SetChange(key.PubKey.GetAddress(ScriptPubKeyType.Legacy, Net))
            .BuildTransaction(sign: true);
        return tx.ToHex();
    }

    [Fact]
    public void P2pkhTransaction_SignedByAddress_Verifies()
    {
        var (key, address, coin) = CreateFundedKey(ScriptPubKeyType.Legacy);
        var hex = BuildSignedSpendHex(key, coin);

        var result = TransactionSignatureVerifier.Verify(hex, address.ToString());

        Assert.Null(result.Error);
        Assert.True(result.IsSignedByAddress);
        Assert.Equal(InputSignatureStatus.SignedByAddress, result.Inputs.Single().Status);
    }

    [Fact]
    public void P2wpkhTransaction_WithSpentAmount_Verifies()
    {
        var (key, address, coin) = CreateFundedKey(ScriptPubKeyType.Segwit);
        var hex = BuildSignedSpendHex(key, coin);

        var result = TransactionSignatureVerifier.Verify(
            hex, address.ToString(), spentAmounts: new Dictionary<int, Money> { [0] = Money.Coins(1m) });

        Assert.True(result.IsSignedByAddress);
        Assert.Equal(InputSignatureStatus.SignedByAddress, result.Inputs.Single().Status);
    }

    [Fact]
    public void P2wpkhTransaction_WithoutSpentAmount_ReportsMissingAmount()
    {
        var (key, address, coin) = CreateFundedKey(ScriptPubKeyType.Segwit);
        var hex = BuildSignedSpendHex(key, coin);

        var result = TransactionSignatureVerifier.Verify(hex, address.ToString());

        Assert.False(result.IsSignedByAddress);
        Assert.Equal(InputSignatureStatus.MissingSpentAmount, result.Inputs.Single().Status);
    }

    [Fact]
    public void P2wpkhTransaction_WithWrongSpentAmount_FailsVerification()
    {
        var (key, address, coin) = CreateFundedKey(ScriptPubKeyType.Segwit);
        var hex = BuildSignedSpendHex(key, coin);

        var result = TransactionSignatureVerifier.Verify(
            hex, address.ToString(), spentAmounts: new Dictionary<int, Money> { [0] = Money.Coins(2m) });

        Assert.False(result.IsSignedByAddress);
        Assert.Equal(InputSignatureStatus.InvalidSignature, result.Inputs.Single().Status);
    }

    [Fact]
    public void TamperedTransaction_FailsVerification()
    {
        var (key, address, coin) = CreateFundedKey(ScriptPubKeyType.Legacy);
        var tampered = Transaction.Parse(BuildSignedSpendHex(key, coin), Net);
        tampered.Outputs[0].Value += Money.Satoshis(1);

        var result = TransactionSignatureVerifier.Verify(tampered.ToHex(), address.ToString());

        Assert.False(result.IsSignedByAddress);
        Assert.Equal(InputSignatureStatus.InvalidSignature, result.Inputs.Single().Status);
    }

    [Fact]
    public void TransactionFromDifferentKey_IsNotAttributedToAddress()
    {
        var (key, _, coin) = CreateFundedKey(ScriptPubKeyType.Legacy);
        var hex = BuildSignedSpendHex(key, coin);
        var unrelatedAddress = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Net);

        var result = TransactionSignatureVerifier.Verify(hex, unrelatedAddress.ToString());

        Assert.False(result.IsSignedByAddress);
        Assert.Equal(InputSignatureStatus.NotFromAddress, result.Inputs.Single().Status);
    }

    [Fact]
    public void UnsignedTransaction_IsReportedAsUnsupported()
    {
        var (key, address, coin) = CreateFundedKey(ScriptPubKeyType.Legacy);
        var unsigned = Net.CreateTransaction();
        unsigned.Inputs.Add(coin.Outpoint);
        unsigned.Outputs.Add(Money.Coins(0.9m), address.ScriptPubKey);

        var result = TransactionSignatureVerifier.Verify(unsigned.ToHex(), address.ToString());

        Assert.False(result.IsSignedByAddress);
        Assert.Equal(InputSignatureStatus.Unsupported, result.Inputs.Single().Status);
    }

    [Fact]
    public void SignTransactionMessage()
    {
        // Real mainnet transaction whose OP_RETURN output carries a text message,
        // signed by the P2PKH address below (change returns to the same address).
        const string hex =
            "0100000001f7744bdbd0cfb0ad2b6380ac614e5f9ae9eb52a1aa46245f903242455019794a" +
            "000000006b483045022100a77a792120ccb38a79ff6a83983086ca19817029250a9f6c6057" +
            "82bba941661e022053f7bc2b53dc708e254274f7e7fc6217253c305050ec2cd8c242798df5" +
            "1d7848012103ff41e2f9b5a2b0c577f23b3a0333f89c61206356826b86807f052a6eb8f4fc" +
            "d0fdffffff02c4090000000000001976a914080701d594c1bda163560b67d254c8aa74bd03" +
            "2f88ac0000000000000000476a4522536f757468204361726f6c696e6120456d657267696e" +
            "672054656368204173736f632e207468696e6b7320626974636f696e206973207072657474" +
            "79206772656174212200000000";
        const string address = "1jSoQG2iMAd8vLmrExeRnwqKvktE65yYi";

        var result = TransactionSignatureVerifier.Verify(hex, address);

        Assert.Null(result.Error);
        Assert.True(result.IsSignedByAddress);
        Assert.Equal(InputSignatureStatus.SignedByAddress, result.Inputs.Single().Status);

        var opReturnPushes = Transaction.Parse(hex, Net).Outputs
            .Select(o => TxNullDataTemplate.Instance.ExtractScriptPubKeyParameters(o.ScriptPubKey))
            .Single(pushes => pushes is not null);
        var message = System.Text.Encoding.UTF8.GetString(opReturnPushes![0]);
        Assert.Equal("\"South Carolina Emerging Tech Assoc. thinks bitcoin is pretty great!\"", message);
    }

    [Fact]
    public void InvalidHex_ReturnsError()
    {
        var address = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Net);

        var result = TransactionSignatureVerifier.Verify("not-hex-at-all", address.ToString());

        Assert.False(result.IsSignedByAddress);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void InvalidAddress_ReturnsError()
    {
        var (key, _, coin) = CreateFundedKey(ScriptPubKeyType.Legacy);
        var hex = BuildSignedSpendHex(key, coin);

        var result = TransactionSignatureVerifier.Verify(hex, "definitely-not-an-address");

        Assert.False(result.IsSignedByAddress);
        Assert.NotNull(result.Error);
    }
}
