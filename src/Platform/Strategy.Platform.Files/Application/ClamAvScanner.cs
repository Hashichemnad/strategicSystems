using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace Strategy.Platform.Files.Application;

public enum ScanOutcome
{
    Clean,
    Infected,
    Unavailable,
}

public interface IVirusScanner
{
    Task<ScanOutcome> ScanAsync(Stream content, string host, int port, CancellationToken cancellationToken);
}

/// <summary>
/// clamd <c>INSTREAM</c> over TCP (in-house client; ClamAV itself runs as a separate optional container, ADR 0007):
/// <c>zINSTREAM\0</c>, chunks prefixed by a 4-byte big-endian length, a zero-length chunk, then <c>stream: OK</c> or <c>… FOUND</c>.
/// </summary>
internal sealed class ClamAvScanner : IVirusScanner
{
    private const int ChunkSize = 64 * 1024;

    public async Task<ScanOutcome> ScanAsync(Stream content, string host, int port, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token).ConfigureAwait(false);
            var buffer = new byte[ChunkSize];
            var length = new byte[4];
            int read;
            while ((read = await content.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                BinaryPrimitives.WriteInt32BigEndian(length, read);
                await stream.WriteAsync(length, timeout.Token).ConfigureAwait(false);
                await stream.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
            }

            BinaryPrimitives.WriteInt32BigEndian(length, 0);
            await stream.WriteAsync(length, timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.ASCII);
            var reply = (await reader.ReadToEndAsync(timeout.Token).ConfigureAwait(false)).TrimEnd('\0', '\n');
            return reply.EndsWith("OK", StringComparison.Ordinal) ? ScanOutcome.Clean
                : reply.EndsWith("FOUND", StringComparison.Ordinal) ? ScanOutcome.Infected
                : ScanOutcome.Unavailable;
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return ScanOutcome.Unavailable;
        }
    }
}
