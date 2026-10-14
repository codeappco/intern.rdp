using System.Net;
using System.Net.WebSockets;
using ClientWebSocket = stajprojesi_uzaktan_baglanti.ResponsiveWebSocket;
using System.Text.Json;

if (args.Length > 0 && args[0] == "--tls")
{
    await TlsTests.Run();
    return;
}
if (args.Length > 0 && args[0] == "--protocol")
{
    await ProtocolTests.Run(new Uri(args[1]));
    return;
}
if (args.Length > 0 && args[0] == "--generation")
{
    await CodeGenerationTests.Run(new Uri(args[1]));
    return;
}
if (args.Length > 0 && args[0] == "--v5")
{
    await Version5Tests.Run(new Uri(args[1]));
    return;
}
if (args.Length > 0 && args[0] == "--v4")
{
    await HeartbeatTests.Run(new Uri(args[1]));
    return;
}
if (args.Length > 0 && args[0] == "--v3")
{
    await LifecycleTests.Run(new Uri(args[1]));
    return;
}
SecurityPolicyTests.Run();
if (args.Length == 0) return;
var endpoint = new Uri(args[0]);
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
    using var stream = new MemoryStream();
    var buffer = new byte[4096];
    WebSocketReceiveResult result;
    do
    {
        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
        if (result.MessageType == WebSocketMessageType.Close) throw new Exception("Unexpected close");
        stream.Write(buffer, 0, result.Count);
    } while (!result.EndOfMessage);
    using var doc = JsonDocument.Parse(stream.ToArray());
    return doc.RootElement.Clone();
}
async Task<ClientWebSocket> Connect(string? origin = null, bool spoofForwarded = false)
{
    var ws = new ClientWebSocket();
    if (origin != null) ws.Options.SetRequestHeader("Origin", origin);
    if (spoofForwarded)
    {
        ws.Options.SetRequestHeader("X-Forwarded-For", "203.0.113." + Random.Shared.Next(1, 255));
        ws.Options.SetRequestHeader("X-Real-IP", "198.51.100." + Random.Shared.Next(1, 255));
    }
    await ws.ConnectAsync(endpoint, token);
    Check((await Read(ws)).GetProperty("tip").GetString() == "kimlik", origin == null ? "native connection without Origin accepted" : "allowlisted Origin accepted");
    return ws;
}
async Task<string> Code(ClientWebSocket host)
{
    await Send(host, new { tip = "kod_iste" });
    return (await Read(host)).GetProperty("kod").GetString()!;
}
async Task<string> Request(ClientWebSocket host, ClientWebSocket viewer, string code)
{
    await Send(viewer, new { tip = "baglan", kod = code });
    var request = await Read(host);
    Check((await Read(viewer)).GetProperty("tip").GetString() == "istek_gonderildi", "owner approval required");
    return request.GetProperty("istekId").GetString()!;
}
async Task Invalid(ClientWebSocket viewer, string code, string name)
{
    await Send(viewer, new { tip = "baglan", kod = code });
    var result = await Read(viewer);
    Check(result.GetProperty("tip").GetString() == "bilgi" &&
        result.GetProperty("mesaj").GetString()!.StartsWith("Kod gecersiz"), name);
}

// Real HTTP upgrade attempts, including same-host, opaque, malformed and empty origins.
using var http = new HttpClient();
foreach (var origin in new[] { "https://evil.example", "http://" + endpoint.Authority, "null", "", "not-an-origin", "https://a.example, https://b.example" })
{
    using var request = new HttpRequestMessage(HttpMethod.Get, new UriBuilder(endpoint) { Scheme = "http" }.Uri);
    request.Headers.TryAddWithoutValidation("Connection", "Upgrade");
    request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
    request.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");
    request.Headers.TryAddWithoutValidation("Sec-WebSocket-Key", Convert.ToBase64String(new byte[16]));
    request.Headers.TryAddWithoutValidation("Origin", origin);
    using var response = await http.SendAsync(request, token);
    Check(response.StatusCode == HttpStatusCode.Forbidden, "Origin rejected before upgrade: " + origin);
}
using var h = await Connect();
using var v = await Connect();
using var other = await Connect("https://trusted.example");
var code = await Code(h);
var id = await Request(h, v, code);
await Send(other, new { tip = "baglanti_cevabi", istekId = id, kabul = true });
Check((await Read(other)).GetProperty("tip").GetString() == "bilgi", "allowlisted Origin grants no approval authority");
await Send(h, new { tip = "baglanti_cevabi", istekId = id, kabul = false });
await Read(h);
Check((await Read(v)).GetProperty("durum").GetString() == "ret", "rejection preserved");
id = await Request(h, v, code);
await Send(other, new { tip = "baglan", kod = code });
Check((await Read(other)).GetProperty("tip").GetString() == "bilgi", "competing request cannot bypass pending approval");
await Send(h, new { tip = "baglanti_cevabi", istekId = id, kabul = true });
Check((await Read(h)).GetProperty("tip").GetString() == "oturum_basladi", "code remains usable after rejection");
await Read(v);
await Invalid(other, code, "code consumed during active session");
await Send(h, new { tip = "baglanti_cevabi", istekId = id, kabul = true });
Check((await Read(h)).GetProperty("tip").GetString() == "bilgi", "duplicate approval rejected");
await Send(v, new { tip = "oturum_bitir", oturumId = id });
await Read(h); await Read(v);
await Invalid(v, code, "consumed code cannot be replayed after session end");

var fresh = await Code(h);
id = await Request(h, v, fresh);
await Send(h, new { tip = "baglanti_cevabi", istekId = id, kabul = true });
await Read(h); await Read(v);
await Send(h, new { tip = "oturum_bitir", oturumId = id });
await Read(h); await Read(v);
Console.WriteLine("PASS fresh code works after completed session");

using (var owner = await Connect())
{
    var orphan = await Code(owner);
    var orphan2 = await Code(owner);
    await owner.CloseAsync(WebSocketCloseStatus.NormalClosure, "test", token);
    await Invalid(v, orphan, "owner disconnect invalidates code");
    await Invalid(v, orphan2, "owner disconnect invalidates all owned codes");
}

// Runner supplies a shortened lifetime to the real server, not a test-only bypass.
var expiring = await Code(h);
var pending = await Request(h, v, expiring);
await Task.Delay(TimeSpan.FromSeconds(int.Parse(args[1]) + 1), token);
await Send(h, new { tip = "baglanti_cevabi", istekId = pending, kabul = true });
Check((await Read(v)).GetProperty("durum").GetString() == "sure_doldu", "expiry rechecked at approval");
await Read(h);
await Invalid(v, expiring, "expired code cannot start another request");

// Hold a real approved session open while exhausting code-guess budgets.
using var activeViewer = await Connect();
var activeCode = await Code(h);
var activeId = await Request(h, activeViewer, activeCode);
await Send(h, new { tip = "baglanti_cevabi", istekId = activeId, kabul = true });
await Read(h); await Read(activeViewer);
using var guesser = await Connect();
for (int i = 0; i < 10; i++)
    await Invalid(guesser, i == 0 ? "LOG_INJECTION_SENTINEL\r\nforged-log" : "00000000", "invalid guesses count toward client budget");
await Send(guesser, new { tip = "baglan", kod = "00000000" });
Check((await Read(guesser)).GetProperty("hata").GetString() == "hiz_siniri", "live socket client limit enforced");
// Limits apply before lookup: a valid code must not bypass throttling.
var unusedCode = await Code(h);
await Send(guesser, new { tip = "baglan", kod = unusedCode });
Check((await Read(guesser)).GetProperty("hata").GetString() == "hiz_siniri", "valid code cannot bypass client limit");
bool ipBlocked = false;
for (int i = 0; i < 31; i++)
{
    using var reconnected = await Connect(spoofForwarded: true);
    await Send(reconnected, new { tip = "baglan", kod = "00000000" });
    var result = await Read(reconnected);
    if (result.TryGetProperty("hata", out var error) && error.GetString() == "hiz_siniri")
    { ipBlocked = true; break; }
    reconnected.Abort();
}
Check(ipBlocked, "reconnect and forged forwarding headers cannot evade IP limit");
await Send(activeViewer, new { tip = "sifreli", oturumId = activeId, surum = 1, veri = "opaque-regression-payload" });
Check((await Read(h)).GetProperty("veri").GetString() == "opaque-regression-payload", "IP limit does not interrupt authorized encrypted relay");
await Send(activeViewer, new { tip = "oturum_bitir", oturumId = activeId });
Check((await Read(h)).GetProperty("tip").GetString() == "oturum_bitti", "session teardown remains authorized under IP limit");
await Read(activeViewer);
foreach (var ws in new[] { h, v, other }) ws.Abort();
