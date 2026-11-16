namespace DocumentProcessing.Domain.Exceptions;

/// <summary>
/// Thrown when a save lost a race with a concurrent change to the same data.
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
