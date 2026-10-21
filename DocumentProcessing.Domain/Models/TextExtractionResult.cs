using DocumentProcessing.Domain.Enums;

namespace DocumentProcessing.Domain.Models;

public class TextExtractionResult
{
    public Guid DocumentId { get; init; }
    public int ExtractionVersion { get; init; }
    public TextExtractionMethod Method { get; init; }
    public IReadOnlyList<ExtractedPage> Pages { get; init; } = new List<ExtractedPage>();
    public string FullText => string.Join("\n--- Page Break ---\n", Pages.Where(p => p.HasText).Select(p => p.Text));
    public int TotalCharacterCount => Pages.Sum(p => p.CharacterCount);
    public IReadOnlyList<string> Warnings { get; init; } = new List<string>();
    public TextExtractionQuality? Quality { get; init; }
    public DateTime ExtractedAtUtc { get; init; } = DateTime.UtcNow;
    public bool IsPartial => Method == TextExtractionMethod.Partial || Method == TextExtractionMethod.Failed;
    public bool HasAnyText => Pages.Any(p => p.HasText);
    public int PageCount => Pages.Count;
}