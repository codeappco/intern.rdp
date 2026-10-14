

var builder = WebApplication.CreateBuilder(args);
var originPolicy = new RemoteDesk.Server.WebSocketOriginPolicy(
    builder.Configuration.GetSection("WebSocket:AllowedOrigins").Get<string[]>() ?? []);
var attemptLimiter = new RemoteDesk.Server.ConnectionAttemptLimiter();
// Independent budgets: generating codes must not consume connection-attempt capacity.
var codeGenerationLimiter = new RemoteDesk.Server.ConnectionAttemptLimiter();
// May be shortened by deployments; never extend the five-minute validity window.
var kodOmru = TimeSpan.FromSeconds(Math.Clamp(
    builder.Configuration.GetValue<int?>("ConnectionCodeLifetimeSeconds") ?? 300, 1, 300));
var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(1),
    KeepAliveTimeout = TimeSpan.FromSeconds(2)
});
var baglantiKodlari =
    new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>();

var kodSahipleri =
    new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

var istemciler =
    new System.Collections.Concurrent.ConcurrentDictionary<
        string, System.Net.WebSockets.WebSocket>();

var gonderimler = new System.Collections.Concurrent.ConcurrentDictionary<
    System.Net.WebSockets.WebSocket, RemoteDesk.Server.RelayOutbox>();
var istekKilidi = new object();
var bekleyenIstekler = new Dictionary<string, (string Isteyen, string Hedef, DateTime Bitis, string Kod)>();
var oturumlar = new Dictionary<string, (string Paylasan, string Izleyen)>();

var mesajKilidi = new SemaphoreSlim(1, 1);

// All code lifecycle operations run under mesajKilidi, including approval and cleanup.
void KoduSil(string kod)
{
    baglantiKodlari.TryRemove(kod, out _);
    kodSahipleri.TryRemove(kod, out _);
}

async Task OturumuBitir(string id)
{
    if (!oturumlar.Remove(id, out var oturum)) return;

    // A stalled/dead socket must not delay notification of the healthy peer.
    await Task.WhenAll(new[] { oturum.Paylasan, oturum.Izleyen }.Select(async kisi =>
    {
        if (istemciler.TryGetValue(kisi, out var hedef))
            try { await MesajGonder(hedef, new { tip = "oturum_bitti", oturumId = id }); }
            catch (Exception) { hedef.Abort(); }
    }));
}

Task MesajGonder(System.Net.WebSockets.WebSocket hedef, object icerik)
{
    if (!gonderimler.TryGetValue(hedef, out var outbox))
        throw new InvalidOperationException("Baglanti kapandi.");
    outbox.Enqueue(icerik);
    return Task.CompletedTask;
}

async Task BaglantiyiTemizle(string istemciId, System.Net.WebSockets.WebSocket socket)
{
    // Idempotent and independent of RequestAborted. Do this BEFORE any close I/O.
    await mesajKilidi.WaitAsync();
    try
    {
        if (!istemciler.TryRemove(istemciId, out _)) return;
        gonderimler.TryRemove(socket, out _);
        foreach (var kod in kodSahipleri.Where(x => x.Value == istemciId).Select(x => x.Key).ToArray())
            KoduSil(kod);
        foreach (var istek in bekleyenIstekler.Where(x =>
            x.Value.Isteyen == istemciId || x.Value.Hedef == istemciId).ToArray())
        {
            bekleyenIstekler.Remove(istek.Key);
            string digerId = istek.Value.Isteyen == istemciId ? istek.Value.Hedef : istek.Value.Isteyen;
            if (istemciler.TryGetValue(digerId, out var diger))
                try { await MesajGonder(diger, new { tip = "baglanti_sonucu", istekId = istek.Key, durum = "sure_doldu" }); }
                catch (Exception) { diger.Abort(); }
        }
        foreach (var id in oturumlar.Where(x => x.Value.Paylasan == istemciId || x.Value.Izleyen == istemciId).Select(x => x.Key).ToArray())
            await OturumuBitir(id);
    }
    finally { mesajKilidi.Release(); }
}

app.Map("/ws", async context =>
{
    // Default: desktop-only. Browser support requires an explicit origin allowlist.
    // Absence/matching Origin is NOT authentication; session authorization remains mandatory.
    if (!originPolicy.Allows(context.Request.Headers.ContainsKey("Origin"),
        context.Request.Headers.Origin.Select(x => x ?? "").ToArray()))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var socket =
        await context.WebSockets.AcceptWebSocketAsync();
    await using var outbox = new RemoteDesk.Server.RelayOutbox(socket);
    gonderimler[socket] = outbox;

    string istemciId = Guid.NewGuid().ToString();
    // Use the transport peer, never untrusted X-Forwarded-For/X-Real-IP values.
    var address = context.Connection.RemoteIpAddress;
    string ipKey = (address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address)?.ToString() ?? "unknown";
    string ipLogTag = attemptLimiter.LogTag(ipKey);
    var clientAttempts = new RemoteDesk.Server.ConnectionAttemptLimiter.Window();
    var clientGenerations = new RemoteDesk.Server.ConnectionAttemptLimiter.Window();
    istemciler[istemciId] = socket;

    try
    {
    await MesajGonder(socket, new
    {
        tip = "kimlik",
        istemciId = istemciId
    });

    app.Logger.LogInformation(
        "Baglanan istemcinin kimligi: {IstemciId}", istemciId);

    app.Logger.LogInformation("Bir uygulama WebSocket ile baglandi.");

    var buffer = new byte[4096];
        while (socket.State == System.Net.WebSockets.WebSocketState.Open)
        {
            string? gelenYazi = await RemoteDesk.Server.WebSocketMessages.ReceiveTextAsync(
                socket, buffer, context.RequestAborted);
            if (gelenYazi == null)
            {
                await BaglantiyiTemizle(istemciId, socket);
                using var kapatmaSuresi = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseOutputAsync(
                    System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                    "Baglanti kapatildi.",
                    kapatmaSuresi.Token);
                break;
            }

            using var belge = System.Text.Json.JsonDocument.Parse(gelenYazi);
            await mesajKilidi.WaitAsync(context.RequestAborted);
            try
            {
            foreach (var eski in baglantiKodlari.Where(x => x.Value <= DateTime.UtcNow).ToArray())
                KoduSil(eski.Key);
            string? mesajTipi = belge.RootElement.GetProperty("tip").GetString();
            if (mesajTipi == "ping")
            {
                if (belge.RootElement.TryGetProperty("zaman", out var zaman) &&
                    zaman.ValueKind == System.Text.Json.JsonValueKind.Number)
                    await MesajGonder(socket, new { tip = "pong", zaman = zaman.GetInt64() });
                continue;
            }
            if (mesajTipi is "e2ee_anahtar" or "sifreli" or "oturum_bitir")
            {
                var veri = belge.RootElement;
                string id = veri.GetProperty("oturumId").GetString() ?? "";
                if (!oturumlar.TryGetValue(id, out var oturum) ||
                    (oturum.Paylasan != istemciId && oturum.Izleyen != istemciId)) continue;
                if (mesajTipi == "oturum_bitir") { await OturumuBitir(id); continue; }
                if (veri.GetProperty("surum").GetInt32() != 1 ||
                    (mesajTipi == "e2ee_anahtar" && gelenYazi.Length > 1024))
                { await OturumuBitir(id); continue; }
                string diger = oturum.Paylasan == istemciId ? oturum.Izleyen : oturum.Paylasan;
                if (istemciler.TryGetValue(diger, out var hedef))
                    try { await MesajGonder(hedef, veri); }
                    catch (Exception) { await OturumuBitir(id); }
                continue;
            }
            if (mesajTipi is "ekran" or "girdi" or "kontrol_izni")
            {
                // No plaintext fallback, even for older clients.
                foreach (var id in oturumlar.Where(x => x.Value.Paylasan == istemciId || x.Value.Izleyen == istemciId).Select(x => x.Key).ToArray())
                    await OturumuBitir(id);
                continue;
            }
            if (belge.RootElement.GetProperty("tip").GetString() == "kod_iste")
            {
                // Check before rotation/cancellation: a rejected generation must leave
                // the current code and any pending owner approval untouched.
                if (!codeGenerationLimiter.TryAcquire(ipKey, clientGenerations, out bool logGenerationLimit))
                {
                    if (logGenerationLimit)
                        app.Logger.LogWarning("ConnectionCodeGenerationLimited PeerTag={PeerTag} ClientId={ClientId}", ipLogTag, istemciId);
                    await MesajGonder(socket, new { tip = "bilgi", hata = "hiz_siniri", islem = "kod_uretimi", mesaj = "Cok fazla kod olusturma istegi. Bir dakika sonra tekrar deneyin." });
                    continue;
                }
                // Keep at most one live code per owner. Generate while the old code
                // still exists so renewal cannot accidentally reissue that code.
                var eskiKodlar = kodSahipleri.Where(x => x.Value == istemciId).Select(x => x.Key).ToArray();
                string kod;

                do
                {
                    kod = System.Security.Cryptography.RandomNumberGenerator
                        .GetInt32(10000000, 100000000)
                        .ToString();
                }
                while (!baglantiKodlari.TryAdd(
                    kod, DateTime.UtcNow.Add(kodOmru)));

                kodSahipleri[kod] = istemciId;
                foreach (var eskiKod in eskiKodlar) KoduSil(eskiKod);
                foreach (var eskiIstek in bekleyenIstekler.Where(x =>
                    x.Value.Hedef == istemciId && eskiKodlar.Contains(x.Value.Kod)).ToArray())
                {
                    bekleyenIstekler.Remove(eskiIstek.Key);
                    if (istemciler.TryGetValue(eskiIstek.Value.Isteyen, out var bekleyen))
                        try { await MesajGonder(bekleyen, new { tip = "baglanti_sonucu", istekId = eskiIstek.Key, durum = "sure_doldu" }); }
                        catch (Exception) { bekleyen.Abort(); }
                }

                await MesajGonder(socket, new
                {
                    tip = "kod_olusturuldu",
                    kod = kod,
                    gecerlilikSaniye = (int)kodOmru.TotalSeconds
                });

            }
            else if (belge.RootElement.GetProperty("tip").GetString() == "baglan")
            {
                if (!attemptLimiter.TryAcquire(ipKey, clientAttempts, out bool logLimit))
                {
                    if (logLimit)
                        app.Logger.LogWarning("ConnectionAttemptLimited PeerTag={PeerTag} ClientId={ClientId}", ipLogTag, istemciId);
                    await MesajGonder(socket, new { tip = "bilgi", hata = "hiz_siniri", mesaj = "Cok fazla baglanti denemesi. Bir dakika sonra tekrar deneyin." });
                    continue;
                }
                string? kod = belge.RootElement.TryGetProperty("kod", out var kodAlani)
                    && kodAlani.ValueKind == System.Text.Json.JsonValueKind.String
                    ? kodAlani.GetString() : null;

                if (kod == null || kod.Length != 8 || kod.Any(c => c < '0' || c > '9') ||
                    !baglantiKodlari.TryGetValue(kod, out var bitis) || bitis <= DateTime.UtcNow ||
                    !kodSahipleri.TryGetValue(kod, out var hedefId) ||
                    !istemciler.TryGetValue(hedefId, out var hedef) ||
                    hedef.State != System.Net.WebSockets.WebSocketState.Open)
                {
                    app.Logger.LogWarning("ConnectionCodeRejected PeerTag={PeerTag} ClientId={ClientId}", ipLogTag, istemciId);
                    await MesajGonder(socket, new { tip = "bilgi", mesaj = "Kod gecersiz, suresi dolmus veya bilgisayar cevrimdisi." });
                    continue;
                }

                if (hedefId == istemciId)
                {
                    await MesajGonder(socket, new { tip = "bilgi", mesaj = "Kendi uygulamana baglanamazsin. Ikinci bir uygulama ac." });
                    continue;
                }

                string istekId = Guid.NewGuid().ToString();
                bool mesgul;
                lock (istekKilidi)
                {
                    foreach (var eski in bekleyenIstekler.Where(x => x.Value.Bitis <= DateTime.UtcNow).ToArray())
                        bekleyenIstekler.Remove(eski.Key);

                    mesgul = oturumlar.Values.Any(x => x.Paylasan == istemciId || x.Izleyen == istemciId || x.Paylasan == hedefId || x.Izleyen == hedefId) || bekleyenIstekler.Values.Any(x =>
                        x.Isteyen == istemciId || x.Hedef == istemciId ||
                        x.Isteyen == hedefId || x.Hedef == hedefId);
                    if (!mesgul)
                        bekleyenIstekler[istekId] = (istemciId, hedefId, DateTime.UtcNow.AddMinutes(1), kod);
                }

                if (mesgul)
                {
                    await MesajGonder(socket, new { tip = "bilgi", mesaj = "Bekleyen bir istek var. Cevabi bekle veya bir dakika sonra tekrar dene." });
                    continue;
                }

                try
                {
                    string isteyenAd = "RemoteDesk-" + istemciId[..8];
                    if (belge.RootElement.TryGetProperty("ad", out var adAlani) && adAlani.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        string ad = adAlani.GetString() ?? "";
                        if (ad.Length <= 32)
                        {
                            ad = new string(ad.Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray()).Trim();
                            if (ad.Length > 0) isteyenAd = ad;
                        }
                    }
                    await MesajGonder(hedef, new { tip = "baglanti_istegi", istekId, isteyenId = istemciId, isteyenAd });
                }
                catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException ||
                    ex is OperationCanceledException || ex is ObjectDisposedException || ex is InvalidOperationException)
                {
                    lock (istekKilidi) bekleyenIstekler.Remove(istekId);
                    await MesajGonder(socket, new { tip = "bilgi", mesaj = "Karsi bilgisayara istek iletilemedi." });
                    continue;
                }

                await MesajGonder(socket, new { tip = "istek_gonderildi", mesaj = "Istek iletildi. Cevap icin 1 dakika bekleniyor." });
            }
            else if (belge.RootElement.GetProperty("tip").GetString() == "baglanti_cevabi")
            {
                if (!belge.RootElement.TryGetProperty("istekId", out var idAlani) ||
                    idAlani.ValueKind != System.Text.Json.JsonValueKind.String ||
                    !belge.RootElement.TryGetProperty("kabul", out var kabulAlani) ||
                    (kabulAlani.ValueKind != System.Text.Json.JsonValueKind.True &&
                     kabulAlani.ValueKind != System.Text.Json.JsonValueKind.False))
                {
                    await MesajGonder(socket, new { tip = "bilgi", mesaj = "Gecersiz cevap." });
                    continue;
                }

                string cevapId = idAlani.GetString() ?? "";
                string? isteyenId = null;
                string? onayKodu = null;
                bool suresiDoldu = false;
                lock (istekKilidi)
                {
                    if (bekleyenIstekler.TryGetValue(cevapId, out var istek) && istek.Hedef == istemciId)
                    {
                        isteyenId = istek.Isteyen;
                        onayKodu = istek.Kod;
                        suresiDoldu = istek.Bitis <= DateTime.UtcNow ||
                            !baglantiKodlari.TryGetValue(istek.Kod, out var kodBitis) || kodBitis <= DateTime.UtcNow ||
                            !kodSahipleri.TryGetValue(istek.Kod, out var sahip) || sahip != istemciId;
                        bekleyenIstekler.Remove(cevapId);
                    }
                }

                if (isteyenId == null)
                {
                    await MesajGonder(socket, new { tip = "bilgi", mesaj = "Istek artik gecerli degil veya cevap yetkin yok." });
                    continue;
                }

                if (!istemciler.TryGetValue(isteyenId, out var isteyenSocket) ||
                    isteyenSocket.State != System.Net.WebSockets.WebSocketState.Open)
                {
                    await MesajGonder(socket, new { tip = "bilgi", mesaj = "Istegi gonderen bilgisayar cevrimdisi." });
                    continue;
                }

                string durum = suresiDoldu ? "sure_doldu" : kabulAlani.GetBoolean() ? "kabul" : "ret";
                if (durum == "kabul")
                {
                    // Consume before any awaited notification. Never restore after a partial send.
                    KoduSil(onayKodu!);
                    oturumlar[cevapId] = (istemciId, isteyenId);
                    try
                    {
                        await MesajGonder(isteyenSocket, new { tip = "oturum_basladi", oturumId = cevapId, rol = "izleyen" });
                        await MesajGonder(socket, new { tip = "oturum_basladi", oturumId = cevapId, rol = "paylasan" });
                    }
                    catch (Exception) { await OturumuBitir(cevapId); }
                    continue;
                }
                try
                {
                    await MesajGonder(isteyenSocket, new { tip = "baglanti_sonucu", istekId = cevapId, durum });
                }
                catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException ||
                    ex is OperationCanceledException || ex is ObjectDisposedException || ex is InvalidOperationException)
                {
                    await MesajGonder(socket, new { tip = "bilgi", mesaj = "Cevap karsi bilgisayara iletilemedi." });
                    continue;
                }

                await MesajGonder(socket, new { tip = "bilgi", mesaj = suresiDoldu
                    ? "Istegin suresi dolmus. Yeni istek gerekiyor."
                    : "Istek reddedildi." });
            }
            }
            finally { mesajKilidi.Release(); }
        }
    }
    catch (RemoteDesk.Server.WebSocketMessageRejectedException ex)
    {
        await BaglantiyiTemizle(istemciId, socket);
        // Do not wait for an uncooperative peer's close handshake before cleanup.
        using var kapatmaSuresi = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await socket.CloseOutputAsync(ex.Status, ex.Message, kapatmaSuresi.Token); }
        catch (Exception) { socket.Abort(); }
    }
    catch (OperationCanceledException)
    {
        // Istek iptal edildi.
    }
    catch (System.Net.WebSockets.WebSocketException)
    {
        app.Logger.LogInformation("WebSocket baglantisi kesildi.");
    }
    catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or
        KeyNotFoundException or FormatException or OverflowException)
    {
        // Malformed JSON/types/numeric ranges affect only this socket. Never log
        // untrusted payloads or infer identity from an optional istemciId field.
        await BaglantiyiTemizle(istemciId, socket);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await socket.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.InvalidPayloadData,
            "Gecersiz mesaj bicimi.", deadline.Token); }
        catch (Exception) { socket.Abort(); }
    }
    finally
    {
        await BaglantiyiTemizle(istemciId, socket);

        app.Logger.LogInformation(
            "Istemci listeden kaldirildi: {IstemciId}", istemciId);
    }

});





app.Run();





