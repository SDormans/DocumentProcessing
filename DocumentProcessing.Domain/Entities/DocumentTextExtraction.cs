namespace DocumentProcessing.Domain.Entities;

public class DocumentTextExtraction
{
    private DocumentTextExtraction()
    {
        // Required by EF Core
    }

    public DocumentTextExtraction(
        Guid id,
        Guid documentId,
        int extractionVersion,
        string extractionMethod,
        string fullText,
        int totalCharacterCount,
        int pageCount,
        bool hasAnyText,
        bool isPartial,
        DateTime extractedAtUtc,
        string? qualityJson = null,
        string? warningsJson = null)
    {
        Id = id;
        DocumentId = documentId;
        ExtractionVersion = extractionVersion;
        ExtractionMethod = extractionMethod;
        FullText = fullText;
        TotalCharacterCount = totalCharacterCount;
        PageCount = pageCount;
        HasAnyText = hasAnyText;
        IsPartial = isPartial;
        ExtractedAtUtc = extractedAtUtc;
        QualityJson = qualityJson;
        WarningsJson = warningsJson;
    }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public int ExtractionVersion { get; private set; }
    public string ExtractionMethod { get; private set; } = string.Empty;
    public string FullText { get; private set; } = string.Empty;
    public int TotalCharacterCount { get; private set; }
    public int PageCount { get; private set; }
    public bool HasAnyText { get; private set; }
    public bool IsPartial { get; private set; }
    public DateTime ExtractedAtUtc { get; private set; }
    public string? QualityJson { get; private set; }
    public string? WarningsJson { get; private set; }

    public void UpdateResult(
        string fullText,
        int totalCharacterCount,
        int pageCount,
        bool hasAnyText,
        bool isPartial,
        string? qualityJson = null,
        string? warningsJson = null)
    {
        FullText = fullText;
        TotalCharacterCount = totalCharacterCount;
        PageCount = pageCount;
        HasAnyText = hasAnyText;
        IsPartial = isPartial;
        QualityJson = qualityJson;
        WarningsJson = warningsJson;
    }
}