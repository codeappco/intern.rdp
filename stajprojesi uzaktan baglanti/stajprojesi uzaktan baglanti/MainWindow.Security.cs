using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace stajprojesi_uzaktan_baglanti;
public partial class MainWindow
{
    private SecureSession? secureSession;
    private PeerVerification? peerVerification;
    private Window? verificationDialog;
    private bool secureActivated;
    private bool secureStarted => peerVerification?.IsActive == true;
    private CancellationTokenSource? secureTimeout;

    private async Task SecureStart(string id, string role)
    {
        if (oturumId != null || (role != "paylasan" && role != "izleyen") ||
            (role == "paylasan" && onaylananIstek != id))
            throw new CryptographicException("Beklenmeyen oturum.");
        OturumuTemizle();
        oturumId = id; paylasan = role == "paylasan";
        localVideoTransport = preferences.VideoTransport;
        secureSession = new SecureSession(id, paylasan);
        DurdurButton.Visibility = Visibility.Visible;
        DurumText.Text = "Guvenli baglanti dogrulamasi bekleniyor. Ekran henuz paylasilmiyor.";
        secureTimeout = new CancellationTokenSource();
        _ = SecureDeadline(id, secureTimeout.Token);
        await MesajGonderAsync(new { tip = "e2ee_anahtar", oturumId = id, surum = 1, anahtar = secureSession.PublicKey });
    }
    private async Task SecureDeadline(string id, CancellationToken token)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), token); if (oturumId == id && !secureStarted) await OturumuDurdur(); }
        catch (OperationCanceledException) { }
    }
    private async Task SecureMessage(JsonElement message)
    {
        if (secureSession == null || message.GetProperty("oturumId").GetString() != oturumId)
            throw new CryptographicException("Beklenmeyen sifreli mesaj.");
        if (message.GetProperty("tip").GetString() == "e2ee_anahtar")
        {
            if (message.GetProperty("surum").GetInt32() != 1) throw new CryptographicException("Desteklenmeyen surum.");
            var session = secureSession;
            session.Establish(message.GetProperty("anahtar").GetString()!);
            peerVerification = new PeerVerification(session.VerificationCode!);
            if (preferences.VerifyPeerKey)
            {
            var dialog = new PeerVerificationDialog(this, session.VerificationCode!, peerVerification);
            verificationDialog = dialog;
            bool accepted;
            try { accepted = dialog.ShowDialog() == true; }
            finally { if (verificationDialog == dialog) verificationDialog = null; }
            if (secureSession != session) return;
            if (!accepted) { await OturumuDurdur(); return; }
            }
            else peerVerification.AcceptRelayTrust();
            await SecureSend(new { tip = "e2ee_hazir", oturumId, kareOnayi = true, webRtcH264 = true, rtcControl = true, aktarim = localVideoTransport });
            SecureActivate();
            return;
        }
        using var decrypted = secureSession.Decrypt(message);
        var value = decrypted.RootElement;
        if (value.GetProperty("oturumId").GetString() != oturumId) throw new CryptographicException("Oturum uyusmuyor.");
        var type = value.GetProperty("tip").GetString();
        var verification = peerVerification ?? throw new CryptographicException("Dogrulama baslamadi.");
        if (type == "e2ee_hazir") { kareOnayiDestegi = value.TryGetProperty("kareOnayi", out var ack) && ack.ValueKind == JsonValueKind.True; peerRtcSupported = value.TryGetProperty("webRtcH264", out var supported) && supported.ValueKind == JsonValueKind.True && value.TryGetProperty("rtcControl", out var control) && control.ValueKind == JsonValueKind.True;
            peerVideoTransport = value.TryGetProperty("aktarim", out var mode) && mode.GetString() == "webrtc" ? "webrtc" : "jpeg";
            verification.PeerReady(); SecureActivate(); return; }
        verification.DemandReceive(type);
        if (type == "rtc_sinyal")
        {
            if (!rtcSession) throw new CryptographicException("Beklenmeyen WebRTC mesaji.");
            var signal = value.GetProperty("signal").Clone();
            RtcPolicy.ValidateSignal(signal, paylasan);
            var init = rtcInitialization;
            if (init != null) await init;
            if (oturumId == value.GetProperty("oturumId").GetString()) rtcView?.Post(new { kind = "signal", signal });
            return;
        }
        if (type == "ekran_alindi" && paylasan)
        {
            if (bekleyenKareSayisi <= 0) throw new CryptographicException("Beklenmeyen kare onayi.");
            bekleyenKareSayisi--;
            if (value.TryGetProperty("gonderimZamani", out var zaman) && zaman.TryGetInt64(out long sent) &&
                sent >= 0 && sent <= Environment.TickCount64)
                kareTurMs = Environment.TickCount64 - sent;
            FpsGuncelle(preferences.Fps);
            return;
        }
        if (!rtcSession && ((type == "kontrol_izni" && !paylasan) || (type == "girdi" && paylasan))) KontrolMesaji(value);
        else if (type == "ekran" && !rtcSession && !paylasan && uzakEkran != null)
        {
            byte[] jpeg = Convert.FromBase64String(value.GetProperty("jpeg").GetString()!);
            try
            {
            var bitmap = await Task.Run(() =>
            {
            using var data = new System.IO.MemoryStream(jpeg, writable: false);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = data; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
            });
            if (uzakEkran == null || oturumId != value.GetProperty("oturumId").GetString()) return;
            uzakEkran.Source = bitmap;
            FpsGuncelle();
            if (kareOnayiDestegi)
            {
                long sent = value.TryGetProperty("gonderimZamani", out var time) && time.TryGetInt64(out long stamp) ? stamp : -1;
                await SecureSend(new { tip = "ekran_alindi", oturumId, gonderimZamani = sent });
            }
            DurumText.Text = "Karsi ekran uctan uca sifreli olarak gosteriliyor.";
            }
            finally { CryptographicOperations.ZeroMemory(jpeg); }
        }
        else throw new CryptographicException("Beklenmeyen mesaj veya rol.");
    }
    private void SecureActivate()
    {
        if (!secureStarted || secureActivated || oturumId == null) return;
        secureActivated = true; secureTimeout?.Cancel();
        OturumBaslat(oturumId, paylasan ? "paylasan" : "izleyen");
    }
    private async Task SecureSend(object value)
    {
        var session = secureSession ?? throw new CryptographicException("Guvenli oturum yok.");
        await gonderimKilidi.WaitAsync();
        try
        {
            if (session != secureSession) throw new CryptographicException("Oturum kapandi.");
            var verification = peerVerification ?? throw new CryptographicException("Dogrulama baslamadi.");
            var packWatch = System.Diagnostics.Stopwatch.StartNew();
            var payload = JsonSerializer.SerializeToElement(value);
            var type = payload.GetProperty("tip").GetString();
            verification.DemandSend(type);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(session.Encrypt(payload));
            if (type == "ekran") { paketHazirlamaMs = packWatch.Elapsed.TotalMilliseconds; sonAgBayt = bytes.Length; }
            var networkWatch = System.Diagnostics.Stopwatch.StartNew();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await socket.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, timeout.Token);
            if (type == "ekran") agYazmaMs = networkWatch.Elapsed.TotalMilliseconds;
            if (type == "e2ee_hazir") verification.LocalReadySent();
        }
        finally { gonderimKilidi.Release(); }
    }
}
