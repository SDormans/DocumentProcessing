using DocumentProcessing.Application.Exceptions;

namespace DocumentProcessing.Application.Exceptions.TextExtraction;

public abstract class TextExtractionException : DocumentProcessingException
{
    protected TextExtractionException(string message) : base(message) { }
    protected TextExtractionException(string message, Exception inner) : base(message, inner) { }
}

public class TextExtractionNotFoundException : TextExtractionException
{
    public TextExtractionNotFoundException(string message) : base(message) { }
}

public class TextExtractionProviderException : TextExtractionException
{
    public TextExtractionProviderException(string message) : base(message) { }
    public TextExtractionProviderException(string message, Exception inner) : base(message, inner) { }
}

public class InvalidPdfException : TextExtractionException
{
    public InvalidPdfException(string message) : base(message) { }
}