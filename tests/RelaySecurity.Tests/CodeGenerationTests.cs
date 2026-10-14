using System.Net.WebSockets;
using System.Text.Json;
using ClientWebSocket = stajprojesi_uzaktan_baglanti.ResponsiveWebSocket;

static class CodeGenerationTests
{
    public static async Task Run(Uri endpoint)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        void Check(bool pass, string name) { if (!pass) throw new Exception(name); Console.WriteLine("PASS " + name); }
        async Task Send(ClientWebSocket ws, object value) =>
            await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value).AsMemory(), WebSocketMessageType.Text, true, token);
        async Task<JsonElement> Read(ClientWebSocket ws)
        {
            using var stream = new MemoryStream();
            var buffer = new byte[4096];
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (result.MessageType == WebSocketMessageType.Close) throw new Exception("Unexpected close");
                stream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            return JsonSerializer.Deserialize<JsonElement>(stream.ToArray());
        }
        async Task<ClientWebSocket> Connect(bool spoof = false)
        {
            var ws = new ClientWebSocket();
            if (spoof) ws.Options.SetRequestHeader("X-Forwarded-For", "203.0.113." + Random.Shared.Next(1, 255));
            await ws.ConnectAsync(endpoint, token); await Read(ws); return ws;
        }
        async Task Limited(ClientWebSocket ws)
        {
            await Send(ws, new { tip = "kod_iste" });
            var result = await Read(ws);
            Check(result.GetProperty("tip").GetString() == "bilgi" && result.GetProperty("hata").GetString() == "hiz_siniri" &&
                result.GetProperty("islem").GetString() == "kod_uretimi", "generation limit returns compatible explicit error");
        }
        using var host = await Connect(); using var viewer = await Connect();
        string first = "", latest = "";
        for (int i = 0; i < 10; i++)
        {
            await Send(host, new { tip = "kod_iste" });
            latest = (await Read(host)).GetProperty("kod").GetString()!;
            if (i == 0) first = latest;
        }
        await Limited(host);
        await Send(viewer, new { tip = "baglan", kod = first });
        Check((await Read(viewer)).GetProperty("mesaj").GetString()!.StartsWith("Kod gecersiz"), "rotation still invalidates earlier codes");
        await Send(viewer, new { tip = "baglan", kod = latest });
        Check((await Read(viewer)).GetProperty("tip").GetString() == "istek_gonderildi", "blocked generation preserves latest code and separate attempt budget");
        var id = (await Read(host)).GetProperty("istekId").GetString();
        await Limited(host);
        await Send(host, new { tip = "baglanti_cevabi", istekId = id, kabul = true });
        Check((await Read(host)).GetProperty("tip").GetString() == "oturum_basladi", "blocked generation preserves pending owner approval");
        await Read(viewer);
        await Send(viewer, new { tip = "oturum_bitir", oturumId = id });
        await Read(host); await Read(viewer);
        await Send(viewer, new { tip = "baglan", kod = latest });
        Check((await Read(viewer)).GetProperty("mesaj").GetString()!.StartsWith("Kod gecersiz"), "approved code remains single-use under generation limit");

        for (int i = 0; i < 20; i++)
        {
            using var reconnected = await Connect(spoof: true);
            await Send(reconnected, new { tip = "kod_iste" });
            Check((await Read(reconnected)).GetProperty("tip").GetString() == "kod_olusturuldu", "shared generation IP budget " + (i + 11));
        }
        using var blocked = await Connect(spoof: true);
        await Limited(blocked);
        await Send(blocked, new { tip = "baglan", kod = "00000000" });
        Check((await Read(blocked)).GetProperty("mesaj").GetString()!.StartsWith("Kod gecersiz"), "generation IP exhaustion does not exhaust connection-attempt budget");
        await Send(host, new { tip = "ping", zaman = 1 });
        Check((await Read(host)).GetProperty("tip").GetString() == "pong", "generation throttling leaves control channel responsive");
    }
}
