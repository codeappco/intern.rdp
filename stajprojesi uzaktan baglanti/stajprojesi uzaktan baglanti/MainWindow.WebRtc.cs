using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace stajprojesi_uzaktan_baglanti;

public partial class MainWindow
{
    private readonly RtcInputOrder rtcInputOrder = new();
    private RtcView? rtcView;
    private Task? rtcInitialization;
    private Window? rtcWindow;
    private bool rtcSession, peerRtcSupported;
    private string localVideoTransport = "jpeg", peerVideoTransport = "jpeg", rtcTurnPassword = "";

    private void RtcTemizle()
    {
        rtcInputOrder.Reset();
        rtcSession = false; peerRtcSupported = false;
        rtcInitialization = null;
        RtcPaylasimAlani.Content = null;
        RtcPaylasimAlani.Visibility = Visibility.Collapsed;
        RtcPaylasimAlani.Height = 340;
        var view = rtcView; rtcView = null; view?.Dispose();
        var window = rtcWindow; rtcWindow = null;
        if (ekranPenceresi == window) ekranPenceresi = null;
        window?.Close();
    }

    private void RtcYerlestir(RtcView view, string id)
    {
        if (paylasan)
        {
            AnaSekmeler.SelectedIndex = 0;
            RtcPaylasimAlani.Height = 340;
            RtcPaylasimAlani.Content = view.Control;
            RtcPaylasimAlani.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() => { if (RtcPaylasimAlani.Content == view.Control) RtcPaylasimAlani.BringIntoView(); }));
            return;
        }
        var window = new Window { Title = "RemoteDesk | H.264 (salt goruntuleme)",
            Width = 1000, Height = 700, Owner = this, Background = Brushes.Black, Content = view.Control };
        rtcWindow = window; ekranPenceresi = window;
        window.Closed += async (_, _) => { if (rtcWindow == window && oturumId == id) await OturumuDurdur(); };
        window.Deactivated += async (_, _) => { if (oturumId == id) { view.Post(new { kind = "pause" }); await GirdiGonder("birak"); } };
        window.Show();
    }

    private async Task RtcBaslat(string id)
    {
        try
        {
            if (!peerRtcSupported) throw new InvalidOperationException("Karsi uygulama WebRTC desteklemiyor. Iki bilgisayari guncelleyin veya JPEG secin.");
            var view = new RtcView(); rtcView = view;
            view.Message += message => RtcMesaji(id, view, message);
            view.Failure += error => RtcHatasi(id, error);
            RtcYerlestir(view, id);
            await view.Initialize(paylasan);
            if (oturumId != id || rtcView != view) { view.Dispose(); return; }
            if (!RtcPolicy.ValidIceUrl(preferences.StunUrl, false) || !RtcPolicy.ValidIceUrl(preferences.TurnUrl, true))
                throw new InvalidOperationException("STUN/TURN adresini kontrol edin.");
            var ice = new List<object>();
            if (preferences.StunUrl.Length > 0) ice.Add(new { urls = preferences.StunUrl });
            if (preferences.TurnUrl.Length > 0)
            {
                if (preferences.TurnUsername.Length == 0 || rtcTurnPassword.Length == 0)
                    throw new InvalidOperationException("TURN kullanici adi ve bu oturum icin parola girin.");
                ice.Add(new { urls = preferences.TurnUrl, username = preferences.TurnUsername, credential = rtcTurnPassword });
            }
            view.Post(new { kind = "init", host = paylasan, fps = preferences.Fps, height = preferences.Height, iceServers = ice });
            DurumText.Text = paylasan ? "Ana ekrandaki dugmeyle birincil ekranini secerek paylasimi baslat." : "H.264 ekran secimi / sifreli baglanti bekleniyor...";
        }
        catch (Exception ex) { await RtcHatasiniBitir(id, $"Video motoru baslatilamadi (0x{ex.HResult:X8}): {ex.Message}"); }
    }
    private async void RtcHatasi(string id, string error) => await RtcHatasiniBitir(id, error);
    private async Task RtcHatasiniBitir(string id, string error)
    {
        if (oturumId != id) return;
        await OturumuDurdur();
        DurumText.Text = "WebRTC: " + error + " JPEG secenegiyle yeniden baglanabilirsiniz.";
        if (!kapaniyor) MessageBox.Show(this, DurumText.Text, "H.264 baglanti hatasi", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private async void RtcMesaji(string id, RtcView view, JsonElement message)
    {
        if (oturumId != id || rtcView != view || !secureStarted) return;
        try
        {
            string? kind = message.GetProperty("kind").GetString();
            if (kind == "signal")
            {
                var signal = message.GetProperty("signal");
                RtcPolicy.ValidateSignal(signal, !paylasan);
                await SecureSend(new { tip = "rtc_sinyal", oturumId = id, signal });
            }
            else if (kind == "error") await RtcHatasiniBitir(id, message.GetProperty("message").GetString() ?? "Baglanti kurulamadi.");
            else if (kind == "ended") await OturumuDurdur();
            else if (kind == "connected")
            {
                if (paylasan) RtcPaylasimAlani.Height = 200;
                DurumText.Text = "H.264 / WebRTC bagli. Goruntu ve kontrol WebRTC ile sifreli.";
            }
            else if (kind == "control-ready")
            {
                if (paylasan) KontrolButton.Visibility = Visibility.Visible;
            }
            else if (kind == "data")
            {
                var data = message.GetProperty("data");
                if (data.GetRawText().Length > 4096 || data.GetProperty("oturumId").GetString() != id)
                    throw new InvalidOperationException("Gecersiz WebRTC kontrol mesaji.");
                string? type = data.GetProperty("tip").GetString();
                if (type == "kontrol_izni" && !paylasan) KontrolMesaji(data);
                else if (type == "girdi" && paylasan)
                {
                    // Check the current permission before advancing any ordering state.
                    if (kontrolIzni.Length == 0 || data.GetProperty("izin").GetString() != kontrolIzni) return;
                    if (rtcInputOrder.Accept(data.GetProperty("eylem").GetString(), data.GetProperty("sira").GetInt64()))
                        KontrolMesaji(data);
                }
                else throw new InvalidOperationException("Beklenmeyen WebRTC kontrol rolu.");
            }
            else if (kind == "stats")
            {
                double fps = message.GetProperty("fps").GetDouble(), kbps = message.GetProperty("kbps").GetDouble();
                string route = message.GetProperty("relay").GetBoolean() ? "TURN" : "Dogrudan";
                int width = message.GetProperty("width").GetInt32(), height = message.GetProperty("height").GetInt32();
                string target = paylasan ? $" / Hedef ust sinir: {preferences.Fps} FPS" : "";
                FpsText.Text = $"H.264 / WebRTC | {fps:0.0} FPS{target} | {kbps:0} kbit/sn | {route} | DTLS-SRTP\nGercek video: {width} x {height} | Kontrol: sifreli DataChannel\nFPS gercek islenen kare sayisidir; sabit ekranda azalabilir, hareketle yukselir.";
            }
            else if (kind == "input" && !paylasan && kontrolIzni.Length > 0 && !UzakGirdi.IsInjected)
            {
                var input = message.GetProperty("input");
                await GirdiGonder(input.GetProperty("eylem").GetString()!,
                    input.GetProperty("x").GetDouble(), input.GetProperty("y").GetDouble(),
                    input.GetProperty("dugme").GetInt32(), input.GetProperty("basili").GetBoolean(),
                    input.GetProperty("tus").GetInt32(), input.GetProperty("delta").GetInt32());
            }
        }
        catch (Exception ex) { await RtcHatasiniBitir(id, ex.Message); }
    }
}
