namespace DocumentProcessing.Application.Exceptions;

public abstract class DocumentProcessingException : Exception
{
    protected DocumentProcessingException(string message) : base(message) { }
    protected DocumentProcessingException(string message, Exception inner) : base(message, inner) { }
}

public class ValidationException : DocumentProcessingException
{
    public IEnumerable<string> Errors { get; }

    public ValidationException(string message, IEnumerable<string> errors)
        : base(message)
    {
        Errors = errors;
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