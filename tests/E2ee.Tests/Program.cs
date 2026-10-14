using System.Net.WebSockets;
using ClientWebSocket = stajprojesi_uzaktan_baglanti.ResponsiveWebSocket;
using System.Security.Cryptography;
using System.Text.Json;
using stajprojesi_uzaktan_baglanti;

VerificationTests.Run();
void Check(bool pass, string name) { if (!pass) throw new Exception(name); Console.WriteLine("PASS " + name); }
void Reject(Action action, string name) { try { action(); } catch (CryptographicException) { Console.WriteLine("PASS " + name); return; } throw new Exception(name); }
JsonElement Envelope(object value) => JsonSerializer.SerializeToElement(value);
using var host = new SecureSession("session", true);
using var viewer = new SecureSession("session", false);
host.Establish(viewer.PublicKey); viewer.Establish(host.PublicKey);
Check(host.VerificationCode == viewer.VerificationCode, "matching verification codes");
var first = Envelope(host.Encrypt(new { tip = "ekran", jpeg = "secret-screen" }));
Check(!first.GetRawText().Contains("secret-screen"), "no plaintext in envelope");
using (var plain = viewer.Decrypt(first)) Check(plain.RootElement.GetProperty("jpeg").GetString() == "secret-screen", "screen roundtrip");
Reject(() => viewer.Decrypt(first), "replay rejected");
Reject(() => host.Decrypt(first), "reflected packet rejected");
var next = Envelope(host.Encrypt(new { tip = "ekran", jpeg = "next" }));
var corrupt = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(next.GetRawText())!;
corrupt["etiket"] = Envelope(Convert.ToBase64String(new byte[16]));
Reject(() => viewer.Decrypt(Envelope(corrupt)), "tampered authentication tag rejected");
using (viewer.Decrypt(next)) { }
var reverse = Envelope(viewer.Encrypt(new { tip = "girdi", tus = 65 }));
using (var plain = host.Decrypt(reverse)) Check(plain.RootElement.GetProperty("tus").GetInt32() == 65, "separate reverse key");
using var outsider = new SecureSession("session", false);
outsider.Establish(host.PublicKey);
Check(outsider.VerificationCode != host.VerificationCode, "substituted peer produces different code");
Reject(() => outsider.Decrypt(first), "outsider cannot decrypt");
Reject(() => host.Establish(viewer.PublicKey), "rekey rejected");
var third = Envelope(host.Encrypt(new { tip = "ekran" }));
corrupt = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(third.GetRawText())!;
corrupt["oturumId"] = Envelope("other-session");
Reject(() => viewer.Decrypt(Envelope(corrupt)), "cross-session message rejected");
var fourth = Envelope(host.Encrypt(new { tip = "ekran" }));
Reject(() => viewer.Decrypt(fourth), "skipped sequence rejected");
using var limitHost = new SecureSession("payload-limit", true);
using var limitViewer = new SecureSession("payload-limit", false);
limitHost.Establish(limitViewer.PublicKey); limitViewer.Establish(limitHost.PublicKey);
Reject(() => limitHost.Encrypt(new { text = new string('a', 1400000) }), "actual client rejects oversized plaintext");
int overhead = JsonSerializer.SerializeToUtf8Bytes(new { text = "" }).Length;
var maximum = Envelope(limitHost.Encrypt(new { text = new string('a', 1400000 - overhead) }));
Check(JsonSerializer.SerializeToUtf8Bytes(maximum).Length <= 2 * 1024 * 1024, "real maximum encrypted client payload fits relay limit");
using (var decrypted = limitViewer.Decrypt(maximum))
    Check(decrypted.RootElement.GetProperty("text").GetString()!.Length == 1400000 - overhead, "oversize rejection does not consume E2EE sequence");
if (args.Length == 0) return;

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var token = deadline.Token;
async Task Send(ClientWebSocket ws, object value)
{
    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
}
async Task<JsonElement> Read(ClientWebSocket ws)
{
    using var stream = new MemoryStream(); var buffer = new byte[4096]; WebSocketReceiveResult result;
    do { result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token); stream.Write(buffer, 0, result.Count); } while (!result.EndOfMessage);
    using var doc = JsonDocument.Parse(stream.ToArray()); return doc.RootElement.Clone();
}
using var h = new ClientWebSocket(); using var v = new ClientWebSocket(); using var stranger = new ClientWebSocket();
foreach (var ws in new[] { h, v, stranger }) { await ws.ConnectAsync(new Uri(args[0]), token); await Read(ws); }
await Send(h, new { tip = "kod_iste" }); var codeMessage = await Read(h); var code = codeMessage.GetProperty("kod").GetString();
Check(codeMessage.GetProperty("gecerlilikSaniye").GetInt32() is > 0 and <= 300, "server supplies actual code lifetime");
await Send(v, new { tip = "baglan", kod = code, ad = "Test Izleyen" }); var request = await Read(h); await Read(v);
Check(request.GetProperty("isteyenAd").GetString() == "Test Izleyen", "display name forwarded in connection request");
var id = request.GetProperty("istekId").GetString()!;
await Send(stranger, new { tip = "baglanti_cevabi", istekId = id, kabul = true });
Check((await Read(stranger)).GetProperty("tip").GetString() == "bilgi", "stranger cannot accept request");
await Send(h, new { tip = "baglanti_cevabi", istekId = id, kabul = true }); await Read(h); await Read(v);
using var hs = new SecureSession(id, true); using var vs = new SecureSession(id, false);
await Send(h, new { tip = "e2ee_anahtar", oturumId = id, surum = 1, anahtar = hs.PublicKey });
vs.Establish((await Read(v)).GetProperty("anahtar").GetString()!);
await Send(v, new { tip = "e2ee_anahtar", oturumId = id, surum = 1, anahtar = vs.PublicKey });
hs.Establish((await Read(h)).GetProperty("anahtar").GetString()!);
Check(hs.VerificationCode == vs.VerificationCode, "relayed key agreement");
await Send(stranger, new { tip = "sifreli", oturumId = id, surum = 1, veri = "forged" });
await Send(h, hs.Encrypt(new { tip = "e2ee_hazir", oturumId = id }));
using (var ready = vs.Decrypt(await Read(v))) Check(ready.RootElement.GetProperty("tip").GetString() == "e2ee_hazir", "outsider injection ignored; encrypted confirmation relayed");
await Send(v, vs.Encrypt(new { tip = "e2ee_hazir", oturumId = id })); using (hs.Decrypt(await Read(h))) { }
string frame = new('a', 40000);
await Send(h, hs.Encrypt(new { tip = "ekran", oturumId = id, jpeg = frame }));
using (var screen = vs.Decrypt(await Read(v))) Check(screen.RootElement.GetProperty("jpeg").GetString() == frame, "fragmented encrypted screen relay");
await Send(v, vs.Encrypt(new { tip = "ekran_alindi", oturumId = id }));
using (var ack = hs.Decrypt(await Read(h))) Check(ack.RootElement.GetProperty("tip").GetString() == "ekran_alindi", "encrypted frame acknowledgement relayed");
await Send(h, hs.Encrypt(new { tip = "kontrol_izni", oturumId = id, izin = "permission" })); using (vs.Decrypt(await Read(v))) { }
await Send(v, vs.Encrypt(new { tip = "girdi", oturumId = id, izin = "permission", tus = 65 }));
using (var input = hs.Decrypt(await Read(h))) Check(input.RootElement.GetProperty("tus").GetInt32() == 65, "encrypted control relay");
await Send(h, new { tip = "ekran", oturumId = id, jpeg = "plaintext" });
Check((await Read(h)).GetProperty("tip").GetString() == "oturum_bitti", "plaintext fallback terminates session");
Check((await Read(v)).GetProperty("tip").GetString() == "oturum_bitti", "plaintext never relayed");
foreach (var ws in new[] { h, v, stranger }) ws.Abort();
