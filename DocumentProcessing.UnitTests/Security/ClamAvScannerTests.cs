using System.Net;
using System.Net.Sockets;
using System.Text;
using DocumentProcessing.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocumentProcessing.UnitTests.Security;

public class ClamAvScannerTests
{
    [Theory]
    [InlineData("stream: OK", true, null)]
    [InlineData("stream: Eicar-Signature FOUND", false, "Eicar-Signature")]
    public void Parses_clamd_replies(string reply, bool clean, string? signature)
    {
        var result = ClamAvScanner.Parse(reply);
        result.IsClean.Should().Be(clean);
        result.Signature.Should().Be(signature);
    }

    [Fact]
    public void Scanner_errors_throw_so_the_file_is_not_processed()
    {
        var act = () => ClamAvScanner.Parse("INSTREAM size limit exceeded. ERROR");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Streams_the_file_with_INSTREAM_framing()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var all = new MemoryStream();
            var buffer = new byte[4096];
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                all.Write(buffer, 0, read);
                var bytes = all.ToArray();
                if (bytes.Length >= 14 && bytes[^4..].All(b => b == 0)) break;
            }
            await stream.WriteAsync(Encoding.ASCII.GetBytes("stream: OK\0"));
            return all.ToArray();
        });

        var scanner = new ClamAvScanner(Options.Create(new MalwareScanningOptions { ClamAvHost = "127.0.0.1", ClamAvPort = port }));
        var result = await scanner.ScanAsync(new MemoryStream("%PDF-1.4"u8.ToArray()));

        result.IsClean.Should().BeTrue();
        var sent = await received;
        Encoding.ASCII.GetString(sent, 0, 10).Should().Be("zINSTREAM\0");
        sent[10..14].Should().Equal(0, 0, 0, 8);
        Encoding.ASCII.GetString(sent, 14, 8).Should().Be("%PDF-1.4");
    }
}
