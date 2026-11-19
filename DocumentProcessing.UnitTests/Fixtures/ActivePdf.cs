using System.Text;

namespace DocumentProcessing.UnitTests.Fixtures;

/// <summary>A one-page PDF with JavaScript in an open action, a page action, a link annotation and an obfuscated name.</summary>
public static class ActivePdf
{
    public static byte[] Create()
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /OpenAction 5 0 R /AA << /WC 5 0 R >> /Names << /JavaScript << /Names [(x) 5 0 R] >> >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 6 0 R >> >> /AA << /O 5 0 R /C 8 0 R >> /Annots [7 0 R] >>",
            "<< /Length 44 >>\nstream\nBT /F1 24 Tf 100 700 Td (Invoice 42) Tj ET\nendstream",
            "<< /Type /Action /S /JavaScript /JS (app.alert\\('pwned'\\)) >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Annot /Subtype /Link /Rect [0 0 100 100] /A 5 0 R >>",
            "<< /Type /Action /S /J#61vaScript /JS (app.launchURL\\('https://example.invalid'\\)) >>",
        };
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
