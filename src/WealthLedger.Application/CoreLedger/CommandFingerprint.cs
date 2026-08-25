namespace WealthLedger.Application.CoreLedger
{
    public sealed record CommandFingerprint(
        string AlgorithmCode,
        int Version,
        string Value);
}
