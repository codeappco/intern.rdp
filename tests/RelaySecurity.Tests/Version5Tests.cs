using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;

// Deliberately use the stock ClientWebSocket, not the desktop transport wrapper.
static class Version5Tests
{
    public static async Task Run(Uri endpoint)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
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
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var stream = new MemoryStream();
            var buffer = new byte[4096];
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
        async Task Invalid(ClientWebSocket ws, string code, string name)
        {
            await Send(ws, new { tip = "baglan", kod = code });
            var response = await Read(ws);
            // A busy/rate-limit response must not be mistaken for a consumed code.
            Check(response.GetProperty("tip").GetString() == "bilgi" &&
                response.GetProperty("mesaj").GetString()!.StartsWith("Kod gecersiz"), name);
        }
        async Task<string> Request(ClientWebSocket h, ClientWebSocket v, string code)
        {
            await Send(v, new { tip = "baglan", kod = code });
            Check((await Read(v)).GetProperty("tip").GetString() == "istek_gonderildi", "request is pending owner approval");
            return (await Read(h)).GetProperty("istekId").GetString()!;
        }
        async Task Accept(ClientWebSocket h, ClientWebSocket v, string id)
        {
            await Send(h, new { tip = "baglanti_cevabi", istekId = id, kabul = true });
            Check((await Read(h)).GetProperty("tip").GetString() == "oturum_basladi", "owner receives approved session");
            Check((await Read(v)).GetProperty("tip").GetString() == "oturum_basladi", "viewer receives approved session");
        }
        async Task Ended(ClientWebSocket ws, string id)
        {
            var result = await Read(ws);
            Check(result.GetProperty("tip").GetString() == "oturum_bitti" && result.GetProperty("oturumId").GetString() == id, "correct session terminated");
            await Send(ws, new { tip = "ping", zaman = 1 });
            Check((await Read(ws)).GetProperty("tip").GetString() == "pong", "survivor usable; no duplicate termination");
        }
        async Task Canceled(ClientWebSocket ws, string id)
        {
            var result = await Read(ws);
            Check(result.GetProperty("tip").GetString() == "baglanti_sonucu" && result.GetProperty("istekId").GetString() == id &&
                result.GetProperty("durum").GetString() == "sure_doldu", "pending peer receives cancellation");
        }
        async Task StaleApproval(ClientWebSocket ws, string id)
        {
            await Send(ws, new { tip = "baglanti_cevabi", istekId = id, kabul = true });
            Check((await Read(ws)).GetProperty("tip").GetString() == "bilgi", "stale approval cannot recreate session");
        }

        using (var h = await Connect())
        using (var v = await Connect())
        {
            var a = await Code(h); var b = await Code(h); var c = await Code(h);
            Check(a != b && b != c, "successive code renewals produce new codes");
            await Invalid(v, a, "first code revoked after renewal");
            await Invalid(v, b, "second code revoked after renewal");
            var pending = await Request(h, v, c);
            var d = await Code(h);
            await Canceled(v, pending);
            await StaleApproval(h, pending);
            await Invalid(v, c, "pending request cannot preserve old code");
            var id = await Request(h, v, d);
            await Accept(h, v, id);
            await Send(v, new { tip = "oturum_bitir", oturumId = id });
            await Ended(h, id); await Ended(v, id);
            await Invalid(v, d, "consumed code stays invalid after explicit session end");
            await StaleApproval(h, id);
        }

        foreach (bool ownerCloses in new[] { true, false })
        {
            using var h = await Connect(); using var v = await Connect();
            var code = await Code(h);
            var id = await Request(h, v, code);
            await Accept(h, v, id);
            var closing = ownerCloses ? h : v;
            var survivor = ownerCloses ? v : h;
            var watch = Stopwatch.StartNew();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            var close = closing.CloseAsync(WebSocketCloseStatus.NormalClosure, "V5 normal close", timeout.Token);
            await Ended(survivor, id);
            Check(watch.Elapsed < TimeSpan.FromSeconds(4), $"normal close notified peer in {watch.ElapsedMilliseconds} ms (owner={ownerCloses})");
            await close;
            await Invalid(survivor, code, "code stays invalid after normal socket close");
            using var next = await Connect();
            var nextId = await Request(survivor, next, await Code(survivor));
            await Accept(survivor, next, nextId);
            await Send(next, new { tip = "oturum_bitir", oturumId = nextId });
            await Ended(survivor, nextId); await Ended(next, nextId);
        }

        foreach (bool ownerCloses in new[] { true, false })
        {
            using var h = await Connect(); using var v = await Connect();
            var code = await Code(h);
            var pending = await Request(h, v, code);
            var closing = ownerCloses ? h : v;
            var survivor = ownerCloses ? v : h;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            var close = closing.CloseAsync(WebSocketCloseStatus.NormalClosure, "pending close", timeout.Token);
            await Canceled(survivor, pending);
            await close;
            if (ownerCloses) await Invalid(survivor, code, "disconnected pending owner leaves no usable code");
            else await StaleApproval(h, pending);
            using var next = await Connect();
            string fresh = ownerCloses ? await Code(survivor) : code;
            var nextId = await Request(survivor, next, fresh);
            await Accept(survivor, next, nextId);
            await Send(next, new { tip = "oturum_bitir", oturumId = nextId });
            await Ended(survivor, nextId); await Ended(next, nextId);
        }
    }
}
