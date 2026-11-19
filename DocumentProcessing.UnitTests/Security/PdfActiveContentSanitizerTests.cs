using System.Text;
using DocumentProcessing.Infrastructure.Security;
using DocumentProcessing.UnitTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig;
using Xunit;

namespace DocumentProcessing.UnitTests.Security;

public class PdfActiveContentSanitizerTests
{
    private readonly PdfActiveContentSanitizer _sanitizer = new(NullLogger<PdfActiveContentSanitizer>.Instance);

    [Fact]
    public void Removes_scripts_actions_and_annotations_but_keeps_the_page()
    {
        var input = ActivePdf.Create();
        Encoding.Latin1.GetString(input).Should().Contain("/JavaScript");

        var output = _sanitizer.Sanitize(input);

        output.Should().NotBeNull();
        var text = Encoding.Latin1.GetString(output!);
        foreach (var name in new[] { "/JavaScript", "/J#61vaScript", "/JS", "/AA", "/OpenAction", "/Annots [ 7" })
            text.Should().NotContain(name);
        using var document = PdfDocument.Open(output);
        document.NumberOfPages.Should().Be(1);
        document.GetPage(1).Text.Should().Be("Invoice 42");
    }

    [Fact]
    public void Leaves_stream_contents_alone()
    {
        var pdf = Encoding.ASCII.GetBytes("<< /JS 1 >>\nstream\n/JS (kept) Tj\nendstream\n<< /AA 2 >>");

        PdfActiveContentSanitizer.NeutraliseActiveNames(pdf);

        Encoding.ASCII.GetString(pdf).Should().Be("<< /XX 1 >>\nstream\n/JS (kept) Tj\nendstream\n<< /XX 2 >>");
    }

    [Fact]
    public void Does_not_touch_names_that_only_start_with_an_active_name()
    {
        var pdf = Encoding.ASCII.GetBytes("<< /JSX 1 /AAA 2 /URI 3 >>");

        PdfActiveContentSanitizer.NeutraliseActiveNames(pdf);

        Encoding.ASCII.GetString(pdf).Should().Be("<< /JSX 1 /AAA 2 /XXX 3 >>");
    }

    [Fact]
    public void Returns_null_for_files_that_cannot_be_parsed()
    {
        _sanitizer.Sanitize(Encoding.ASCII.GetBytes("%PDF-1.4 not really")).Should().BeNull();
    }
}
