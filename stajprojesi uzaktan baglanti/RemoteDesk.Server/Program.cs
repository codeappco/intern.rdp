



var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string sonKod = "Henuz kod olusturulmadi";
app.UseWebSockets();
var baglantiKodlari =
    new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>();

var kodSahipleri =
    new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

var istemciler =
    new System.Collections.Concurrent.ConcurrentDictionary<
        string, System.Net.WebSockets.WebSocket>();

var gonderimKilitleri = new System.Runtime.CompilerServices.ConditionalWeakTable<
    System.Net.WebSockets.WebSocket, SemaphoreSlim>();
var istekKilidi = new object();
var bekleyenIstekler = new Dictionary<string, (string Isteyen, string Hedef, DateTime Bitis)>();
var oturumlar = new Dictionary<string, (string Paylasan, string Izleyen)>();
var kontrolIzinleri = new Dictionary<string, string>();
var mesajKilidi = new SemaphoreSlim(1, 1);

async Task OturumuBitir(string id)
{
    if (!oturumlar.Remove(id, out var oturum)) return;
    kontrolIzinleri.Remove(id);
    foreach (var kisi in new[] { oturum.Paylasan, oturum.Izleyen })
        if (istemciler.TryGetValue(kisi, out var hedef))
            try { await MesajGonder(hedef, new { tip = "oturum_bitti", oturumId = id }); }
            catch (Exception) { hedef.Abort(); }
}

async Task MesajGonder(System.Net.WebSockets.WebSocket hedef, object icerik)
{
    var kilit = gonderimKilitleri.GetValue(hedef, _ => new SemaphoreSlim(1, 1));
    await kilit.WaitAsync();
    try
    {
        byte[] veri = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(icerik);
        using var zamanAsimi = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await hedef.SendAsync(new ArraySegment<byte>(veri),
            System.Net.WebSockets.WebSocketMessageType.Text, true, zamanAsimi.Token);
    }
    finally
    {
        kilit.Release();
    }
}

app.MapGet("/baglanti-kodu/{kod}", (string kod) =>
{
    if (baglantiKodlari.TryGetValue(kod, out var bitisZamani)
        && bitisZamani > DateTime.UtcNow)
    {
        return Results.Ok(new { gecerli = true });
    }

    return Results.Ok(new { gecerli = false });
});

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var socket =
        await context.WebSockets.AcceptWebSocketAsync();

    string istemciId = Guid.NewGuid().ToString();
    istemciler[istemciId] = socket;

    await MesajGonder(socket, new
    {
        tip = "kimlik",
        istemciId = istemciId
    });

    app.Logger.LogInformation(
        "Baglanan istemcinin kimligi: {IstemciId}", istemciId);

    app.Logger.LogInformation("Bir uygulama WebSocket ile baglandi.");

    var buffer = new byte[4096];
    using var gelenMesaj = new System.IO.MemoryStream();

    try
    {
        while (socket.State == System.Net.WebSockets.WebSocketState.Open)
        {
            var sonuc = await socket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                context.RequestAborted);

            if (sonuc.MessageType ==
                System.Net.WebSockets.WebSocketMessageType.Close)
            {
                await socket.CloseOutputAsync(
                    System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                    "Baglanti kapatildi.",
                    context.RequestAborted);

                break;
            }

            gelenMesaj.Write(buffer, 0, sonuc.Count);

            if (gelenMesaj.Length > 2 * 1024 * 1024 || sonuc.MessageType != System.Net.WebSockets.WebSocketMessageType.Text)
            {
                await socket.CloseAsync(
                    System.Net.WebSockets.WebSocketCloseStatus.MessageTooBig,
                    "Mesaj cok buyuk.",
                    context.RequestAborted);
                break;
            }

            if (!sonuc.EndOfMessage)
            {
                continue;
            }

            string gelenYazi =
                System.Text.Encoding.UTF8.GetString(gelenMesaj.ToArray());

            gelenMesaj.SetLength(0);

            using var belge = System.Text.Json.JsonDocument.Parse(gelenYazi);
            await mesajKilidi.WaitAsync(context.RequestAborted);
            try
            {
            string? mesajTipi = belge.RootElement.GetProperty("tip").GetString();
            if (mesajTipi == "kontrol_izni" || mesajTipi == "girdi")
            {
                var veri = belge.RootElement;
                string id = veri.GetProperty("oturumId").GetString() ?? "";
                if (!oturumlar.TryGetValue(id, out var oturum)) continue;
                if (mesajTipi == "kontrol_izni" && oturum.Paylasan == istemciId)
                {
                    string izin = veri.GetProperty("izin").GetString() ?? "";
                    if (izin.Length != 0 && !Guid.TryParse(izin, out _)) continue;
                    kontrolIzinleri[id] = izin;
                    if (istemciler.TryGetValue(oturum.Izleyen, out var hedef))
                        await MesajGonder(hedef, new { tip = "kontrol_izni", oturumId = id, izin });
                }
                else if (mesajTipi == "girdi" && oturum.Izleyen == istemciId &&
                    kontrolIzinleri.TryGetValue(id, out var izin) && izin.Length > 0 &&
                    veri.GetProperty("izin").GetString() == izin && gelenYazi.Length < 2048 &&
                    istemciler.TryGetValue(oturum.Paylasan, out var hedef))
                {
                    try { await MesajGonder(hedef, veri); }
                    catch (Exception) { await OturumuBitir(id); }
                }
                continue;
            }
            if (mesajTipi == "ekran" || mesajTipi == "oturum_bitir")
            {
                string id = belge.RootElement.GetProperty("oturumId").GetString() ?? "";
                if (!oturumlar.TryGetValue(id, out var oturum)) continue;
                if (mesajTipi == "oturum_bitir")
                {
                    if (oturum.Paylasan == istemciId || oturum.Izleyen == istemciId)
                        await OturumuBitir(id);
                }
                else if (oturum.Paylasan == istemciId && istemciler.TryGetValue(oturum.Izleyen, out var izleyen))
                {
                    try { await MesajGonder(izleyen, belge.RootElement); }
                    catch (Exception) { await OturumuBitir(id); }
                }
                continue;
            }
            if (belge.RootElement.GetProperty("tip").GetString() == "kod_iste")
            {
                string kod;

                do
                {
                    kod = System.Security.Cryptography.RandomNumberGenerator
                        .GetInt32(10000000, 100000000)
                        .ToString();
                }
                while (!baglantiKodlari.TryAdd(
                    kod, DateTime.UtcNow.AddMinutes(5)));

                kodSahipleri[kod] = istemciId;

                await MesajGonder(socket, new
                {
                    tip = "kod_olusturuldu",
                    kod = kod
                });

            }
            else if (belge.RootElement.GetProperty("tip").GetString() == "baglan")
            {
                string? kod = belge.RootElement.TryGetProperty("kod", out var kodAlani)
                    && kodAlani.ValueKind == System.Text.Json.JsonValueKind.String
                    ? kodAlani.GetString() : null;

                if (kod == null || kod.Length != 8 || kod.Any(c => c < '0' || c > '9') ||
                    !baglantiKodlari.TryGetValue(kod, out var bitis) || bitis <= DateTime.UtcNow ||
                    !kodSahipleri.TryGetValue(kod, out var hedefId) ||
                    !istemciler.TryGetValue(hedefId, out var hedef) ||
                    hedef.State != System.Net.WebSockets.WebSocketState.Open)
                {
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
                        bekleyenIstekler[istekId] = (istemciId, hedefId, DateTime.UtcNow.AddMinutes(1));
                }

                if (mesgul)
                {
                    await MesajGonder(socket, new { tip = "bilgi", mesaj = "Bekleyen bir istek var. Cevabi bekle veya bir dakika sonra tekrar dene." });
                    continue;
                }

                try
                {
                    await MesajGonder(hedef, new { tip = "baglanti_istegi", istekId, isteyenId = istemciId });
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
                bool suresiDoldu = false;
                lock (istekKilidi)
                {
                    if (bekleyenIstekler.TryGetValue(cevapId, out var istek) && istek.Hedef == istemciId)
                    {
                        isteyenId = istek.Isteyen;
                        suresiDoldu = istek.Bitis <= DateTime.UtcNow;
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
    catch (OperationCanceledException)
    {
        // Istek iptal edildi.
    }
    catch (System.Net.WebSockets.WebSocketException)
    {
        app.Logger.LogInformation("WebSocket baglantisi kesildi.");
    }
    catch (System.Text.Json.JsonException) { }
    catch (InvalidOperationException) { }
    catch (KeyNotFoundException) { }
    finally
    {
        istemciler.TryRemove(istemciId, out _);
        await mesajKilidi.WaitAsync();
        try
        {
            foreach (var id in oturumlar.Where(x => x.Value.Paylasan == istemciId || x.Value.Izleyen == istemciId).Select(x => x.Key).ToArray())
                await OturumuBitir(id);
        }
        finally { mesajKilidi.Release(); }
        lock (istekKilidi)
        {
            foreach (var istek in bekleyenIstekler.Where(x =>
                x.Value.Isteyen == istemciId || x.Value.Hedef == istemciId).ToArray())
                bekleyenIstekler.Remove(istek.Key);
        }

        app.Logger.LogInformation(
            "Istemci listeden kaldirildi: {IstemciId}", istemciId);
    }

});


app.MapPost("/baglanti-kodu", () =>
{
    string kod = System.Security.Cryptography.RandomNumberGenerator
        .GetInt32(10000000, 100000000)
        .ToString();
    app.Logger.LogInformation("Uretilen deneme kodu: {Kod}", kod);
    baglantiKodlari[kod] = DateTime.UtcNow.AddMinutes(5);

    sonKod = kod;


    return Results.Ok(new { kod });

});




app.MapGet("/", () => sonKod);



app.Run();




