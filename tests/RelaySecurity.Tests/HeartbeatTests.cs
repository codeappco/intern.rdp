using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using ClientWebSocket = stajprojesi_uzaktan_baglanti.ResponsiveWebSocket;

static class HeartbeatTests
{
    public static async Task Run(Uri endpoint)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
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
        async Task<ClientWebSocket> Connect(Uri uri)
        {
            var ws = new ClientWebSocket();
            await ws.ConnectAsync(uri, token);
            await Read(ws);
            return ws;
        }
        using var independent = await Connect(endpoint);
        // Simulate a human leaving an approval dialog open without consuming messages.
        await Task.Delay(TimeSpan.FromSeconds(6), token);
        await Send(independent, new { tip = "ping", zaman = 1 });
        Check((await Read(independent)).GetProperty("tip").GetString() == "pong", "desktop transport answers heartbeat while UI consumption is paused");

        foreach (var scenario in new[] { (Owner: true, Drop: 3), (Owner: false, Drop: 3), (Owner: true, Drop: 1), (Owner: false, Drop: 2) })
        {
            await using var proxy = new BlackholeProxy(endpoint);
            using var lost = await Connect(proxy.Endpoint);
            using var survivor = await Connect(endpoint);
            var owner = scenario.Owner ? lost : survivor;
            var viewer = scenario.Owner ? survivor : lost;
            await Send(owner, new { tip = "kod_iste" });
            var code = (await Read(owner)).GetProperty("kod").GetString();
            await Send(viewer, new { tip = "baglan", kod = code });
            var id = (await Read(owner)).GetProperty("istekId").GetString();
            await Read(viewer);
            await Send(owner, new { tip = "baglanti_cevabi", istekId = id, kabul = true });
            await Read(owner); await Read(viewer);

            // Keep both TCP sockets open, but drop bytes; neither FIN nor RST is sent.
            var watch = Stopwatch.StartNew();
            proxy.Drop = scenario.Drop;
            await Send(independent, new { tip = "ping", zaman = 2 });
            Check((await Read(independent)).GetProperty("tip").GetString() == "pong", "unrelated client remains responsive during silent loss");
            var ended = await Read(survivor);
            Check(ended.GetProperty("tip").GetString() == "oturum_bitti" && ended.GetProperty("oturumId").GetString() == id && watch.Elapsed < TimeSpan.FromSeconds(5),
                $"silent TCP loss detected and peer notified in {watch.ElapsedMilliseconds} ms (owner={scenario.Owner}, drop={scenario.Drop})");
            await Send(survivor, new { tip = "ping", zaman = 3 });
            Check((await Read(survivor)).GetProperty("tip").GetString() == "pong", "exactly one session-end notification after heartbeat cleanup");
            // A fresh approved session proves the old membership no longer makes it busy.
            await Send(survivor, new { tip = "kod_iste" });
            var fresh = (await Read(survivor)).GetProperty("kod").GetString();
            await Send(independent, new { tip = "baglan", kod = fresh });
            Check((await Read(independent)).GetProperty("tip").GetString() == "istek_gonderildi", "heartbeat cleanup releases session membership");
            var request = (await Read(survivor)).GetProperty("istekId").GetString();
            await Send(survivor, new { tip = "baglanti_cevabi", istekId = request, kabul = false });
            await Read(survivor); await Read(independent);
        }
    }

    private sealed class BlackholeProxy : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly Task run;
        private int drop;
        public int Drop { set => Volatile.Write(ref drop, value); }
        public Uri Endpoint { get; }
        public BlackholeProxy(Uri target)
        {
            listener.Start();
            Endpoint = new Uri($"ws://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}{target.AbsolutePath}");
            run = Run(target);
        }
        private async Task Run(Uri target)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                using var server = new TcpClient();
                await server.ConnectAsync(target.Host, target.Port, stop.Token);
                await Task.WhenAll(Copy(client.GetStream(), server.GetStream(), 1), Copy(server.GetStream(), client.GetStream(), 2));
            }
            catch (Exception) when (stop.IsCancellationRequested) { }
            catch (IOException) { }
            catch (SocketException) { }
        }
        private async Task Copy(NetworkStream source, NetworkStream target, int direction)
        {
            var buffer = new byte[8192];
            while (true)
            {
                int count = await source.ReadAsync(buffer.AsMemory(), stop.Token);
                if (count == 0) return;
                if ((Volatile.Read(ref drop) & direction) == 0)
                    await target.WriteAsync(buffer.AsMemory(0, count), stop.Token);
            }
        }
        public async ValueTask DisposeAsync()
        {
            stop.Cancel(); listener.Stop(); await run; stop.Dispose();
        }
    }
}
