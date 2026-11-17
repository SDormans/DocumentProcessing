namespace DocumentProcessing.Infrastructure.Ocr;

/// <summary>
/// OCR for pages without a text layer: <c>pdftoppm</c> renders the page, the <c>tesseract</c> CLI reads it.
/// </summary>
public class OcrOptions
{
    public const string SectionName = "Ocr";

    public bool Enabled { get; set; } = true;

    /// <summary>Tesseract language codes joined with <c>+</c>; each needs its traineddata installed.</summary>
    public string Language { get; set; } = "eng+nld";

    public int Dpi { get; set; } = 300;

    /// <summary>Pages with fewer native characters than this are sent to OCR.</summary>
    public int MinNativeCharactersPerPage { get; set; } = 10;

    /// <summary>Upper bound on OCR'd pages per document, to keep processing inside the queue lease.</summary>
    public int MaxPages { get; set; } = 20;

    /// <summary>Timeout for rendering or recognising a single page.</summary>
    public int PageTimeoutSeconds { get; set; } = 60;

    public string TesseractPath { get; set; } = "tesseract";

    public string PdftoppmPath { get; set; } = "pdftoppm";
}
