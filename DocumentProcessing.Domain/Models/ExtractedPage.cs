namespace DocumentProcessing.Domain.Models;

public class ExtractedPage
{
    public int PageNumber { get; init; }
    public string Text { get; init; } = string.Empty;
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public int CharacterCount => Text.Length;
}