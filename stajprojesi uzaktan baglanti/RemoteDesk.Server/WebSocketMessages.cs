using System.Net.WebSockets;
using System.Text;

namespace RemoteDesk.Server;

public sealed class WebSocketMessageRejectedException(WebSocketCloseStatus status, string message) : Exception(message)
{
    public WebSocketCloseStatus Status { get; } = status;
}

public static class WebSocketMessages
{
    public const int MaxMessageBytes = 2 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool started = false;
        while (true)
        {
            WebSocketReceiveResult result;
            try { result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new WebSocketMessageRejectedException(WebSocketCloseStatus.PolicyViolation, "Mesaj tamamlanmadi.");
            }
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text)
                throw new WebSocketMessageRejectedException(WebSocketCloseStatus.InvalidMessageType, "Yalnizca metin mesajlari desteklenir.");
            // Check the total before writing; fragmented messages share the same cap.
            if (result.Count > MaxMessageBytes - message.Length)
                throw new WebSocketMessageRejectedException(WebSocketCloseStatus.MessageTooBig, "Mesaj cok buyuk.");
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                try { return StrictUtf8.GetString(message.GetBuffer(), 0, (int)message.Length); }
                catch (DecoderFallbackException)
                {
                    throw new WebSocketMessageRejectedException(WebSocketCloseStatus.InvalidPayloadData, "Gecersiz UTF-8.");
                }
            }
            if (!started)
            {
                // A peer cannot retain a partial message indefinitely by trickling fragments.
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                started = true;
            }
        }
    }
}
