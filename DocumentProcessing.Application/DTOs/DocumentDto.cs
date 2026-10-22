namespace DocumentProcessing.Application.DTOs;

public class DocumentDto
{
    public Guid Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class UploadDocumentResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class ErrorResponse
{
    public string TraceId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public IEnumerable<string>? Errors { get; set; }
}

public class DocumentFileDto
{
    public Stream Stream { get; set; } = Stream.Null;
    public string ContentType { get; set; } = "application/pdf";
    public string FileName { get; set; } = string.Empty;
}