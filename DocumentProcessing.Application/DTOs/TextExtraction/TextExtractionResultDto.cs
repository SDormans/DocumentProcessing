namespace DocumentProcessing.Application.DTOs.TextExtraction;

public class ExtractedPageDto
{
    public int PageNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool HasText { get; set; }
    public int CharacterCount { get; set; }
}

public class TextExtractionQualityDto
{
    public double AverageConfidence { get; set; }
    public int PagesWithLowConfidence { get; set; }
    public bool IsReliable { get; set; }
}

public class TextExtractionResultDto
{
    public Guid DocumentId { get; set; }
    public int ExtractionVersion { get; set; }
    public string Method { get; set; } = string.Empty;
    public IReadOnlyList<ExtractedPageDto> Pages { get; set; } = new List<ExtractedPageDto>();
    public string FullText { get; set; } = string.Empty;
    public int TotalCharacterCount { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = new List<string>();
    public TextExtractionQualityDto? Quality { get; set; }
    public DateTime ExtractedAtUtc { get; set; }
    public bool IsPartial { get; set; }
    public bool HasAnyText { get; set; }
    public int PageCount { get; set; }
}

public class ExtractTextRequest
{
    public Guid DocumentId { get; set; }
    public int? ExtractionVersion { get; set; }
    public bool ForceOcr { get; set; } = false;
}

public class ExtractTextResponse
{
    public Guid DocumentId { get; set; }
    public int ExtractionVersion { get; set; }
    public string Method { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool IsPartial { get; set; }
    public int PageCount { get; set; }
    public int TotalCharacterCount { get; set; }
    public bool HasAnyText { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = new List<string>();
    public DateTime ExtractedAtUtc { get; set; }
}