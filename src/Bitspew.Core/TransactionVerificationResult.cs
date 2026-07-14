namespace Bitspew.Core;

/// <summary>Outcome of checking a single transaction input against a target address.</summary>
public enum InputSignatureStatus
{
    /// <summary>The input spends from the target address and its signature is valid.</summary>
    SignedByAddress,

    /// <summary>The input spends from the target address but its signature does not verify.</summary>
    InvalidSignature,

    /// <summary>The input spends from some other address.</summary>
    NotFromAddress,

    /// <summary>
    /// SegWit signatures commit to the value of the output being spent (BIP143), which is not
    /// present in the transaction itself. The spent amount must be supplied to verify this input.
    /// </summary>
    MissingSpentAmount,

    /// <summary>The input's script type is not supported (e.g. P2SH, P2WSH, taproot, or unsigned).</summary>
    Unsupported,
}

/// <summary>Per-input verification detail.</summary>
public sealed record InputVerificationResult(int InputIndex, InputSignatureStatus Status, string? Detail = null);

/// <summary>
/// Result of verifying a transaction against an address. <see cref="IsSignedByAddress"/> is true
/// when at least one input spends from the address and every input spending from it verifies.
/// </summary>
public sealed record TransactionVerificationResult(
    bool IsSignedByAddress,
    IReadOnlyList<InputVerificationResult> Inputs,
    string? Error = null)
{
    public static TransactionVerificationResult Failed(string error) => new(false, [], error);
}
