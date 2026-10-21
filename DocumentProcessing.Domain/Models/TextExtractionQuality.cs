namespace DocumentProcessing.Domain.Models;

public class TextExtractionQuality
{
    public double AverageConfidence { get; init; }
    public int PagesWithLowConfidence { get; init; }
    public bool IsReliable => AverageConfidence >= 0.8 && PagesWithLowConfidence == 0;
}