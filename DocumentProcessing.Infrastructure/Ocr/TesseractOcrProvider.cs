using DocumentProcessing.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tesseract;

namespace DocumentProcessing.Infrastructure.Ocr;

public class TesseractOcrProvider : IOcrProvider
{
    private readonly ILogger<TesseractOcrProvider> _logger;
    private readonly string _tessDataPath;
    private readonly string _language;

    public TesseractOcrProvider(
        ILogger<TesseractOcrProvider> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _tessDataPath = configuration["Ocr:TessDataPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "tessdata");
        _language = configuration["Ocr:Language"] ?? "eng";

        IsAvailable = Directory.Exists(_tessDataPath);
        if (!IsAvailable)
        {
            _logger.LogWarning(
                "Tesseract tessdata path not found: {TessDataPath}. OCR will not be available.",
                _tessDataPath);
        }
    }

    public bool IsAvailable { get; }

    public async Task<string> ExtractTextFromImageAsync(
        Stream imageStream,
        int pageNumber,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("Tesseract OCR provider is not available. Skipping OCR for page {PageNumber}.", pageNumber);
            return string.Empty;
        }

        try
        {
            using var engine = new TesseractEngine(_tessDataPath, _language, EngineMode.Default);
            using var img = Pix.LoadFromMemory(ReadStreamBytes(imageStream));
            using var page = engine.Process(img);

            var text = page.GetText();
            var confidence = page.GetMeanConfidence();

            _logger.LogDebug(
                "OCR completed for page {PageNumber}: {CharacterCount} chars, confidence={Confidence:P0}",
                pageNumber, text?.Length ?? 0, confidence);

            return text ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tesseract OCR failed for page {PageNumber}", pageNumber);
            throw new Application.Exceptions.TextExtraction.TextExtractionProviderException(
                $"OCR failed for page {pageNumber}: {ex.Message}", ex);
        }
    }

    private static byte[] ReadStreamBytes(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}