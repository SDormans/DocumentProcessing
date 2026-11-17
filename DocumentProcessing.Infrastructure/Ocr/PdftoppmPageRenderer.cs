using System.Globalization;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Infrastructure.Ocr;

public interface IPdfPageRenderer
{
    /// <summary>Renders one page (1-based) of the PDF at <paramref name="pdfPath"/> to a PNG.</summary>
    Task<Stream> RenderPageAsync(string pdfPath, int pageNumber, CancellationToken cancellationToken = default);
}

public sealed class PdftoppmPageRenderer : IPdfPageRenderer
{
    private readonly OcrOptions _options;

    public PdftoppmPageRenderer(IOptions<OcrOptions> options)
    {
        _options = options.Value;
    }

    public async Task<Stream> RenderPageAsync(string pdfPath, int pageNumber, CancellationToken cancellationToken = default)
    {
        var page = pageNumber.ToString(CultureInfo.InvariantCulture);
        var prefix = Path.Combine(Path.GetTempPath(), $"dp-ocr-{Guid.NewGuid():N}");
        var png = prefix + ".png";
        try
        {
            await ExternalProcess.RunAsync(_options.PdftoppmPath,
                ["-f", page, "-l", page, "-r", _options.Dpi.ToString(CultureInfo.InvariantCulture), "-png", "-singlefile", pdfPath, prefix],
                TimeSpan.FromSeconds(_options.PageTimeoutSeconds), cancellationToken);
            return new MemoryStream(await File.ReadAllBytesAsync(png, cancellationToken));
        }
        finally
        {
            File.Delete(png);
        }
    }
}
