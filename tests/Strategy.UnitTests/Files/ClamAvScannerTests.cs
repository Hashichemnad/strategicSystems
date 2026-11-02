using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Strategy.Platform.Files.Application;

namespace Strategy.UnitTests.Files;

/// <summary>The clamd INSTREAM protocol against a fake clamd on a local port.</summary>
public class ClamAvScannerTests
{
    [Theory]
    [InlineData("stream: OK\0", ScanOutcome.Clean)]
    [InlineData("stream: Eicar-Test-Signature FOUND\0", ScanOutcome.Infected)]
    [InlineData("INSTREAM size limit exceeded. ERROR\0", ScanOutcome.Unavailable)]
    public async Task Scanner_streams_the_content_in_chunks_and_reads_the_verdict(string reply, ScanOutcome expected)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var content = Encoding.ASCII.GetBytes(new string('x', 150_000));
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var command = new byte[10];
            await stream.ReadExactlyAsync(command);
            Encoding.ASCII.GetString(command).ShouldBe("zINSTREAM\0");
            var received = 0;
            var length = new byte[4];
            while (true)
            {
                await stream.ReadExactlyAsync(length);
                var size = BinaryPrimitives.ReadInt32BigEndian(length);
                if (size == 0)
                {
                    break;
                }

                size.ShouldBeLessThanOrEqualTo(64 * 1024);
                await stream.ReadExactlyAsync(new byte[size]);
                received += size;
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes(reply));
            return received;
        });

        var outcome = await new ClamAvScanner().ScanAsync(new MemoryStream(content), "127.0.0.1", port, CancellationToken.None);

        outcome.ShouldBe(expected);
        (await server).ShouldBe(content.Length);
    }

    [Fact]
    public async Task An_unreachable_scanner_is_reported_as_unavailable()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        (await new ClamAvScanner().ScanAsync(new MemoryStream([1]), "127.0.0.1", port, CancellationToken.None)).ShouldBe(ScanOutcome.Unavailable);
    }
}
