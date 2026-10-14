using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace RemoteDesk.Server;

// One ordered writer per socket. Enqueuing never waits on network I/O while the
// relay state lock is held. Slow receivers fail closed without stalling others.
public sealed class RelayOutbox : IAsyncDisposable
{
    private readonly WebSocket socket;
    private readonly Channel<byte[]> queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource stop = new();
    private readonly Task writer;
    private long buffered;
    public RelayOutbox(WebSocket socket) { this.socket = socket; writer = WriteAsync(); }

    public void Enqueue(object value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (Interlocked.Add(ref buffered, bytes.Length) <= 8 * 1024 * 1024 && queue.Writer.TryWrite(bytes)) return;
        Interlocked.Add(ref buffered, -bytes.Length);
        socket.Abort();
        throw new InvalidOperationException("Relay receiver is unavailable or too slow.");
    }

    private async Task WriteAsync()
    {
        try
        {
            await foreach (var bytes in queue.Reader.ReadAllAsync(stop.Token))
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
                }
                finally { Interlocked.Add(ref buffered, -bytes.Length); }
            }
        }
        catch (Exception) { socket.Abort(); }
        finally
        {
            queue.Writer.TryComplete();
            while (queue.Reader.TryRead(out var bytes)) Interlocked.Add(ref buffered, -bytes.Length);
        }
    }

    public async ValueTask DisposeAsync()
    {
        queue.Writer.TryComplete();
        stop.Cancel();
        await writer;
        stop.Dispose();
    }
}
