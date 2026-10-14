using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ClientWebSocket = stajprojesi_uzaktan_baglanti.ResponsiveWebSocket;

static class ProtocolTests
{
    public static async Task Run(Uri endpoint)
    {
        await TlsTests.Run();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        void Check(bool pass, string name) { if (!pass) throw new Exception(name); Console.WriteLine("PASS " + name); }
        async Task Raw(ClientWebSocket ws, string json) => await ws.SendAsync(Encoding.UTF8.GetBytes(json).AsMemory(), WebSocketMessageType.Text, true, token);
        Task Send(ClientWebSocket ws, object value) => Raw(ws, JsonSerializer.Serialize(value));
        async Task<JsonElement> Read(ClientWebSocket ws)
        {
            var bytes = new byte[4096]; using var stream = new MemoryStream(); WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(bytes), token);
                if (result.MessageType == WebSocketMessageType.Close) throw new Exception("Unexpected close");
                stream.Write(bytes, 0, result.Count);
            } while (!result.EndOfMessage);
            return JsonSerializer.Deserialize<JsonElement>(stream.ToArray());
        }
        async Task<(ClientWebSocket Socket, string Id)> Connect()
        {
            var ws = new ClientWebSocket(); await ws.ConnectAsync(endpoint, token);
            return (ws, (await Read(ws)).GetProperty("istemciId").GetString()!);
        }
        var host = await Connect(); var viewer = await Connect(); var outsider = await Connect();
        using var h = host.Socket; using var v = viewer.Socket; using var other = outsider.Socket;
        await Send(h, new { tip = "kod_iste", istemciId = new { untrusted = true } });
        var code = (await Read(h)).GetProperty("kod").GetString();
        await Send(v, new { tip = "baglan", kod = code, istemciId = host.Id });
        var pending = await Read(h); await Read(v);
        Check(pending.GetProperty("isteyenId").GetString() == viewer.Id, "extra istemciId cannot impersonate requester");
        var id = pending.GetProperty("istekId").GetString();
        await Send(other, new { tip = "baglanti_cevabi", istekId = id, kabul = true, istemciId = host.Id });
        Check((await Read(other)).GetProperty("tip").GetString() == "bilgi", "spoofed istemciId cannot approve another owner's request");
        await Send(h, new { tip = "baglanti_cevabi", istekId = id, kabul = true, istemciId = viewer.Id });
        await Read(h); await Read(v);
        foreach (object? extra in new object?[] { null, true, 123, "spoof", new { nested = "ignored" } })
        {
            await Send(h, new { tip = "ping", zaman = 1, istemciId = extra });
            Check((await Read(h)).GetProperty("tip").GetString() == "pong", "well-formed extra istemciId is ignored without disconnect");
        }
        await Send(other, new { tip = "sifreli", oturumId = id, surum = 1, veri = "forged", istemciId = host.Id });
        await Send(other, new { tip = "oturum_bitir", oturumId = id, istemciId = host.Id });
        await Send(other, new { tip = "ping", zaman = 2 }); await Read(other);
        async Task Healthy()
        {
            await Send(h, new { tip = "sifreli", oturumId = id, surum = 1, veri = "authorized-envelope", istemciId = outsider.Id });
            var result = await Read(v);
            Check(result.GetProperty("veri").GetString() == "authorized-envelope", "other socket cannot inject/terminate; legitimate relay remains isolated");
        }
        await Healthy();
        foreach (string malformed in new[] { "{", "[]", "{\"tip\":123}", "{\"tip\":\"ping\",\"zaman\":1e100}", "{\"tip\":\"baglanti_cevabi\",\"istekId\":[]}" })
        {
            var bad = await Connect(); using var ws = bad.Socket;
            await Raw(ws, malformed);
            if (malformed.Contains("baglanti_cevabi"))
                Check((await Read(ws)).GetProperty("tip").GetString() == "bilgi", "invalid approval shape returns protocol error without affecting peers");
            else
            {
                var close = await ws.ReceiveAsync(new ArraySegment<byte>(new byte[4096]), token);
                Check(close.MessageType == WebSocketMessageType.Close && close.CloseStatus == WebSocketCloseStatus.InvalidPayloadData,
                    "malformed JSON/type/number closes only offending connection with 1007");
            }
            await Healthy();
        }
        await Send(h, new { tip = "oturum_bitir", oturumId = id, istemciId = outsider.Id });
        Check((await Read(h)).GetProperty("tip").GetString() == "oturum_bitti", "actual participant can terminate with ignored extra identity field");
        await Read(v);
    }
}
