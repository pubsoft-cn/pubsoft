namespace PubSoft.EntityFrameworkCore;

/// <summary>
/// Raised when commit outcome is ambiguous and the caller did not provide a verification probe.
/// The executor throws instead of blindly retrying a possibly committed transaction.
/// </summary>
public sealed class EfCoreTransactionCommitVerificationException : Exception
{
    /// <summary>Creates the exception with the original commit/flush failure when available.</summary>
    public EfCoreTransactionCommitVerificationException(Exception? innerException)
        : base(
            "The EF transaction commit outcome is ambiguous and no verifySucceeded probe was supplied; " +
            "the operation was not retried blindly.",
            innerException)
    {
    }
}
