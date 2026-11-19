using System.Text;
using DocumentProcessing.Application.Services;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace DocumentProcessing.Infrastructure.Security;

/// <summary>
/// Rebuilds the PDF page by page with PdfPig, which drops the document catalog (open actions, name trees with
/// JavaScript, embedded files, AcroForm/XFA) and annotations. PdfPig still copies page dictionaries verbatim, so the
/// rebuilt file's dictionaries are then scanned and active-content names are overwritten with same-length dummy
/// names. Keeping the length keeps the cross-reference table valid. PdfPig writes every dictionary itself, so
/// escaped names such as <c>/J#61vaScript</c> arrive normalised.
/// </summary>
public sealed class PdfActiveContentSanitizer : IPdfSanitizer
{
    private static readonly string[] ActiveNames =
    [
        "JavaScript", "JS", "AA", "OpenAction", "Launch", "SubmitForm", "ImportData", "GoToR", "GoToE",
        "RichMedia", "EmbeddedFile", "EmbeddedFiles", "XFA", "AcroForm", "URI", "Sound", "Movie", "Rendition"
    ];

    private readonly ILogger<PdfActiveContentSanitizer> _logger;

    public PdfActiveContentSanitizer(ILogger<PdfActiveContentSanitizer> logger) => _logger = logger;

    public byte[]? Sanitize(byte[] pdf)
    {
        try
        {
            byte[] rebuilt;
            using (var document = PdfDocument.Open(pdf))
            using (var builder = new PdfDocumentBuilder())
            {
                var options = new PdfDocumentBuilder.AddPageOptions { KeepAnnotations = false };
                for (var page = 1; page <= document.NumberOfPages; page++)
                    builder.AddPage(document, page, options);
                rebuilt = builder.Build();
            }

            NeutraliseActiveNames(rebuilt);
            using (var check = PdfDocument.Open(rebuilt))
                _ = check.NumberOfPages;
            return rebuilt;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PDF could not be rebuilt without active content.");
            return null;
        }
    }

    /// <summary>Overwrites active-content names outside streams, e.g. <c>/JavaScript</c> becomes <c>/XXXXXXXXXX</c>.</summary>
    internal static void NeutraliseActiveNames(byte[] pdf)
    {
        var position = 0;
        while (position < pdf.Length)
        {
            var streamStart = IndexOf(pdf, "stream", position);
            var dictionaryEnd = streamStart < 0 ? pdf.Length : streamStart;
            NeutraliseRange(pdf, position, dictionaryEnd);
            if (streamStart < 0)
                break;
            var streamEnd = IndexOf(pdf, "endstream", streamStart + "stream".Length);
            position = streamEnd < 0 ? pdf.Length : streamEnd + "endstream".Length;
        }
    }

    private static void NeutraliseRange(byte[] pdf, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (pdf[i] != (byte)'/')
                continue;
            var nameEnd = i + 1;
            while (nameEnd < end && !IsDelimiter(pdf[nameEnd]))
                nameEnd++;
            var name = Encoding.ASCII.GetString(pdf, i + 1, nameEnd - i - 1);
            if (ActiveNames.Contains(name, StringComparer.Ordinal))
                pdf.AsSpan(i + 1, nameEnd - i - 1).Fill((byte)'X');
            i = nameEnd - 1;
        }
    }

    private static bool IsDelimiter(byte b) =>
        b is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or (byte)'\f' or 0
            or (byte)'/' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'(' or (byte)')' or (byte)'{' or (byte)'}' or (byte)'%';

    private static int IndexOf(byte[] haystack, string needle, int start)
    {
        var bytes = Encoding.ASCII.GetBytes(needle);
        var index = haystack.AsSpan(start).IndexOf(bytes);
        return index < 0 ? -1 : start + index;
    }
}
