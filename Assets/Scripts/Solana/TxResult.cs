namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Structured result for on-chain transaction attempts.
    /// Replaces the previous pattern of returning null on failure.
    /// </summary>
    public readonly struct TxResult
    {
        public bool Success { get; }
        public string Signature { get; }
        public string Error { get; }

        private TxResult(bool success, string signature, string error)
        {
            Success = success;
            Signature = signature;
            Error = error;
        }

        public static TxResult Ok(string signature) =>
            new TxResult(true, signature, null);

        public static TxResult Fail(string error) =>
            new TxResult(false, null, error);

        public override string ToString() =>
            Success
                ? $"TxResult.Ok({Signature})"
                : $"TxResult.Fail({Error ?? "unknown"})";
    }
}
