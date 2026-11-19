using DocumentProcessing.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Infrastructure.Ocr;

/// <summary>
/// Runs the <c>tesseract</c> CLI on a page image. The worker image installs it with the
/// <c>eng</c> and <c>nld</c> language data.
/// </summary>
public sealed class TesseractCliOcrProvider : IOcrProvider
{
    private readonly OcrOptions _options;
    private readonly ILogger<TesseractCliOcrProvider> _logger;
    private readonly Lazy<bool> _isAvailable;

    public TesseractCliOcrProvider(IOptions<OcrOptions> options, ILogger<TesseractCliOcrProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _isAvailable = new Lazy<bool>(() =>
        {
            var available = ExternalProcess.IsOnPath(_options.TesseractPath) && ExternalProcess.IsOnPath(_options.PdftoppmPath);
            if (!available)
                _logger.LogWarning("OCR is unavailable: {Tesseract} or {Pdftoppm} was not found on PATH.", _options.TesseractPath, _options.PdftoppmPath);
            return available;
        });
    }

    public bool IsAvailable => _isAvailable.Value;

    public async Task<string> ExtractTextFromImageAsync(Stream imageStream, int pageNumber, CancellationToken cancellationToken = default)
    {
        var image = Path.Combine(Path.GetTempPath(), $"dp-ocr-{Guid.NewGuid():N}.png");
        try
        {
            await using (var file = File.Create(image))
                await imageStream.CopyToAsync(file, cancellationToken);

            var text = await ExternalProcess.RunAsync(_options.TesseractPath,
                [image, "stdout", "-l", _options.Language],
                TimeSpan.FromSeconds(_options.PageTimeoutSeconds), cancellationToken);
            _logger.LogDebug("OCR read {CharacterCount} characters from page {PageNumber}.", text.Length, pageNumber);
            return text;
        }
        finally
        {
            File.Delete(image);
        }
    }
}
