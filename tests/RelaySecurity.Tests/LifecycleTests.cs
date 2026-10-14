using System.Net.WebSockets;
using ClientWebSocket = stajprojesi_uzaktan_baglanti.ResponsiveWebSocket;
using System.Text;
using System.Text.Json;

static class LifecycleTests
{
    public static async Task Run(Uri endpoint)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        void Check(bool pass, string name)
        {
            if (!pass) throw new Exception(name);
            Console.WriteLine("PASS " + name);
        }
        async Task Send(ClientWebSocket ws, object value) =>
            await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value).AsMemory(), WebSocketMessageType.Text, true, token);
        async Task<JsonElement> Read(ClientWebSocket ws)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var stream = new MemoryStream();
            var buffer = new byte[8192];
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                if (result.MessageType == WebSocketMessageType.Close) throw new Exception("Unexpected close");
                stream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            return JsonSerializer.Deserialize<JsonElement>(stream.ToArray());
        }
        async Task<ClientWebSocket> Connect()
        {
            var ws = new ClientWebSocket();
            await ws.ConnectAsync(endpoint, token);
            await Read(ws);
            return ws;
        }
        async Task<string> Code(ClientWebSocket ws)
        {
            await Send(ws, new { tip = "kod_iste" });
            return (await Read(ws)).GetProperty("kod").GetString()!;
        }
        async Task<string> Request(ClientWebSocket owner, ClientWebSocket viewer, string code)
        {
            await Send(viewer, new { tip = "baglan", kod = code });
            var response = await Read(viewer);
            Check(response.GetProperty("tip").GetString() == "istek_gonderildi", "new request requires approval");
            return (await Read(owner)).GetProperty("istekId").GetString()!;
        }
        async Task<string> Session(ClientWebSocket owner, ClientWebSocket viewer)
        {
            var id = await Request(owner, viewer, await Code(owner));
            await Send(owner, new { tip = "baglanti_cevabi", istekId = id, kabul = true });
            Check((await Read(owner)).GetProperty("tip").GetString() == "oturum_basladi", "owner starts approved session");
            await Read(viewer);
            return id;
        }
        async Task Ended(ClientWebSocket ws, string id, string name)
        {
            var result = await Read(ws);
            Check(result.GetProperty("tip").GetString() == "oturum_bitti" && result.GetProperty("oturumId").GetString() == id, name);
            await Send(ws, new { tip = "ping", zaman = 123 });
            Check((await Read(ws)).GetProperty("tip").GetString() == "pong", "termination delivered once; surviving socket remains usable");
        }
        async Task Closed(ClientWebSocket ws, WebSocketCloseStatus expected)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(new byte[4096]), timeout.Token);
            Check(result.MessageType == WebSocketMessageType.Close && result.CloseStatus == expected, "protocol close: " + expected);
            // Deliberately do not acknowledge: server cleanup must not wait for us.
        }

        using var host = await Connect();
        using var viewer = await Connect();
        var oldCode = await Code(host);
        var pending = await Request(host, viewer, oldCode);
        var newCode = await Code(host);
        Check(newCode != oldCode, "renewal produces a different code");
        Check((await Read(viewer)).GetProperty("durum").GetString() == "sure_doldu", "renewal cancels pending old-code request");
        await Send(host, new { tip = "baglanti_cevabi", istekId = pending, kabul = true });
        Check((await Read(host)).GetProperty("tip").GetString() == "bilgi", "stale approval cannot resurrect renewed code");
        await Send(viewer, new { tip = "baglan", kod = oldCode });
        Check((await Read(viewer)).GetProperty("mesaj").GetString()!.StartsWith("Kod gecersiz"), "old code rejected immediately after renewal");
        var id = await Request(host, viewer, newCode);
        await Send(host, new { tip = "baglanti_cevabi", istekId = id, kabul = true });
        await Read(host); await Read(viewer);
        viewer.Abort();
        await Ended(host, id, "abrupt viewer disconnect notifies owner within five seconds");
        using var replacement = await Connect();
        id = await Session(host, replacement);
        host.Abort();
        await Ended(replacement, id, "abrupt owner disconnect notifies viewer within five seconds");
        using var nextOwner = await Connect();
        id = await Session(nextOwner, replacement);

        // Exactly 2 MiB accepted; fragments are also split inside a UTF-8 code point.
        const int limit = 2 * 1024 * 1024;
        string prefix = "{\"tip\":\"sifreli\",\"oturumId\":\"" + id + "\",\"surum\":1,\"veri\":\"";
        string suffix = "\"}";
        byte[] prefixBytes = Encoding.UTF8.GetBytes(prefix);
        byte[] payload = Encoding.UTF8.GetBytes(prefix + "ü" + new string('a', limit - prefixBytes.Length - 2 - suffix.Length) + suffix);
        Check(payload.Length == limit, "boundary message is exactly 2097152 bytes");
        int split = prefixBytes.Length + 1;
        await nextOwner.SendAsync(payload.AsMemory(0, split), WebSocketMessageType.Text, false, token);
        for (int offset = split; offset < payload.Length; offset += 8192)
        {
            int count = Math.Min(8192, payload.Length - offset);
            await nextOwner.SendAsync(payload.AsMemory(offset, count), WebSocketMessageType.Text, offset + count == payload.Length, token);
        }
        var relayed = await Read(replacement);
        Check(relayed.GetProperty("veri").GetString() == "ü" + new string('a', limit - prefixBytes.Length - 2 - suffix.Length), "exact-limit fragmented UTF-8 payload relayed intact");
        await Send(nextOwner, new { tip = "ping", zaman = 1 });
        Check((await Read(nextOwner)).GetProperty("tip").GetString() == "pong", "message size resets between complete messages");

        // Send 2 MiB without EndOfMessage, then one extra byte. No partial relay.
        for (int offset = 0; offset < payload.Length; offset += 8192)
            await nextOwner.SendAsync(payload.AsMemory(offset, Math.Min(8192, payload.Length - offset)), WebSocketMessageType.Text, false, token);
        await nextOwner.SendAsync(new byte[] { 32 }.AsMemory(), WebSocketMessageType.Text, true, token);
        await Closed(nextOwner, WebSocketCloseStatus.MessageTooBig);
        await Ended(replacement, id, "oversized fragmented message never relayed; session cleaned without close acknowledgement");

        using var binaryOwner = await Connect();
        id = await Session(binaryOwner, replacement);
        await binaryOwner.SendAsync(new byte[] { 1 }.AsMemory(), WebSocketMessageType.Binary, true, token);
        await Closed(binaryOwner, WebSocketCloseStatus.InvalidMessageType);
        await Ended(replacement, id, "binary input terminates session without plaintext relay");

        using var partialOwner = await Connect();
        id = await Session(partialOwner, replacement);
        await partialOwner.SendAsync(Encoding.UTF8.GetBytes("{\"tip\":\"sifreli\"").AsMemory(), WebSocketMessageType.Text, false, token);
        partialOwner.Abort();
        await Ended(replacement, id, "disconnect mid-fragment discards partial message and cleans session");
        using var invalidUtf8 = await Connect();
        await invalidUtf8.SendAsync(new byte[] { 0xff }.AsMemory(), WebSocketMessageType.Text, true, token);
        await Closed(invalidUtf8, WebSocketCloseStatus.InvalidPayloadData);
        replacement.Abort();
    }
}
