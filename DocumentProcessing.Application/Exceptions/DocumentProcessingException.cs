namespace DocumentProcessing.Application.Exceptions;

public abstract class DocumentProcessingException : Exception
{
    protected DocumentProcessingException(string message) : base(message) { }
    protected DocumentProcessingException(string message, Exception inner) : base(message, inner) { }
}

public enum ValidationFailureKind
{
    Invalid,
    TooLarge,
    UnsupportedMediaType
}

public class ValidationException : DocumentProcessingException
{
    public IEnumerable<string> Errors { get; }

    /// <summary>The most significant failure, so the API can pick 413/415 without parsing messages.</summary>
    public ValidationFailureKind Kind { get; }

    public ValidationException(string message, IEnumerable<string> errors, ValidationFailureKind kind = ValidationFailureKind.Invalid)
        : base(message)
    {
        Errors = errors;
        Kind = kind;
    }
}

public class NotFoundException : DocumentProcessingException
{
    public NotFoundException(string message) : base(message) { }
}

public class StorageException : DocumentProcessingException
{
    public StorageException(string message) : base(message) { }
    public StorageException(string message, Exception inner) : base(message, inner) { }
}