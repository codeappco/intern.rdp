using System.Net.WebSockets;
using System.Threading.Channels;

namespace stajprojesi_uzaktan_baglanti;

// Keep the transport receive pending even while the UI waits for human approval.
// ClientWebSocket processes WebSocket Ping/Pong in ReceiveAsync, not on the UI.
public sealed class ResponsiveWebSocket : WebSocket
{
    private readonly ClientWebSocket inner = new();
    private readonly Channel<(byte[] Bytes, WebSocketReceiveResult Result)> inbox =
        Channel.CreateBounded<(byte[], WebSocketReceiveResult)>(new BoundedChannelOptions(512)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private (byte[] Bytes, WebSocketReceiveResult Result)? current;
    private int offset;
    private int state = (int)WebSocketState.None;
    public ClientWebSocketOptions Options => inner.Options;
    public override WebSocketState State => (WebSocketState)Volatile.Read(ref state);
    public override WebSocketCloseStatus? CloseStatus => inner.CloseStatus;
    public override string? CloseStatusDescription => inner.CloseStatusDescription;
    public override string? SubProtocol => inner.SubProtocol;

    public async Task ConnectAsync(Uri uri, CancellationToken token)
    {
        Volatile.Write(ref state, (int)WebSocketState.Connecting);
        await inner.ConnectAsync(uri, token).ConfigureAwait(false);
        Volatile.Write(ref state, (int)WebSocketState.Open);
        _ = Task.Run(PumpAsync);
    }

    private async Task PumpAsync()
    {
        try
        {
            while (true)
            {
                var bytes = new byte[8192];
                var result = await inner.ReceiveAsync(new ArraySegment<byte>(bytes), CancellationToken.None).ConfigureAwait(false);
                // Bound retained data at 4 MiB, including when a dialog remains open.
                if (!inbox.Writer.TryWrite((bytes, result)))
                    throw new WebSocketException("Incoming message queue is full.");
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
            inbox.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            inner.Abort();
            inbox.Writer.TryComplete(ex);
        }
    }

    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
    {
        if (current == null)
        {
            try { current = await inbox.Reader.ReadAsync(token).ConfigureAwait(false); }
            catch (ChannelClosedException ex) { throw new WebSocketException("Connection ended.", ex); }
            offset = 0;
        }
        var chunk = current.Value;
        int count = Math.Min(buffer.Count, chunk.Result.Count - offset);
        chunk.Bytes.AsSpan(offset, count).CopyTo(buffer.AsSpan());
        offset += count;
        bool complete = offset == chunk.Result.Count;
        if (complete) current = null;
        if (chunk.Result.MessageType == WebSocketMessageType.Close)
            Volatile.Write(ref state, (int)WebSocketState.CloseReceived);
        return new WebSocketReceiveResult(count, chunk.Result.MessageType, complete && chunk.Result.EndOfMessage,
            chunk.Result.CloseStatus, chunk.Result.CloseStatusDescription);
    }

    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token) => inner.SendAsync(buffer, type, end, token);
    public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => inner.CloseOutputAsync(status, description, token);
    public override async Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken token)
    {
        // The pump owns the only transport ReceiveAsync; consume its close result.
        await inner.CloseOutputAsync(status, description, token).ConfigureAwait(false);
        var buffer = new byte[8192];
        while ((await ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false)).MessageType != WebSocketMessageType.Close) { }
        Volatile.Write(ref state, (int)WebSocketState.Closed);
    }
    public override void Abort() { Volatile.Write(ref state, (int)WebSocketState.Aborted); inner.Abort(); }
    public override void Dispose() { inner.Dispose(); Volatile.Write(ref state, (int)WebSocketState.Closed); }
}
