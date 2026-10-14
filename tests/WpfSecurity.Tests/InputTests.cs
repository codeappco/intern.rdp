using System.Reflection;
using System.Text.Json;
using stajprojesi_uzaktan_baglanti;

static class InputTests
{
    public static void Run()
    {
        void Check(bool pass, string name) { if (!pass) throw new Exception(name); Console.WriteLine("PASS " + name); }
        JsonElement Message(object value) => JsonSerializer.SerializeToElement(value);
        int mouseEvents = 0;
        var input = new UzakGirdi((_, _, _, _) => mouseEvents++, (_, _) => { });
        input.Apply(Message(new { eylem = "dugme", x = 0.5, y = 0.5, dugme = 99, basili = true }));
        input.Apply(Message(new { eylem = "tekerlek", x = 0.5, y = 0.5, delta = 1201 }));
        try { input.Apply(Message(new { eylem = "dugme", x = 0.5, y = 0.5 })); }
        catch (KeyNotFoundException) { }
        Check(mouseEvents == 0, "malformed mouse commands have no partial movement side effect");
        bool failRelease = true; int releaseAttempts = 0;
        var retry = new UzakGirdi(keySender: (_, down) =>
        { if (!down) { releaseAttempts++; if (failRelease) throw new InvalidOperationException(); } });
        retry.Apply(Message(new { eylem = "tus", tus = 65, basili = true }));
        retry.Release(); failRelease = false; retry.Release(); retry.Release();
        Check(releaseAttempts == 2, "failed key release remains tracked for retry and successful release is idempotent");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(MainWindow w, string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(w, value);
        void Call(MainWindow w, string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(w, args);
        var window = new MainWindow();
        var gate = new PeerVerification("0123456789ABCDEF0123456789ABCDEF");
        gate.ConfirmPeerCode("0123456789ABCDEF0123456789ABCDEF"); gate.LocalReadySent(); gate.PeerReady();
        int keyEvents = 0;
        Set(window, "uzakGirdi", new UzakGirdi((_, _, _, _) => mouseEvents++, (_, _) => keyEvents++));
        Set(window, "peerVerification", gate); Set(window, "oturumId", "input-test"); Set(window, "paylasan", true);
        var old = Message(new { tip = "girdi", oturumId = "input-test", izin = "old", eylem = "tus", tus = 65, basili = true });
        Call(window, "KontrolMesaji", old);
        Check(keyEvents == 0, "real WPF input requires explicit owner permission");
        Set(window, "kontrolIzni", "old");
        Call(window, "KontrolMesaji", old);
        Check(keyEvents == 1, "authorized input reaches fake Windows sink");
        Call(window, "KontrolTemizle");
        Call(window, "KontrolMesaji", old);
        Check(keyEvents == 2, "revocation immediately releases held key and rejects queued old input");
        Set(window, "kontrolIzni", "new");
        Call(window, "KontrolMesaji", old);
        Check(keyEvents == 2, "regrant cannot revive previous permission token");
        using var rtcView = new RtcView();
        Set(window, "rtcView", rtcView); Set(window, "rtcSession", true);
        var rtcOld = Message(new { kind = "data", data = new { tip = "girdi", oturumId = "input-test", izin = "old", eylem = "tus", tus = 66, basili = true, sira = 1 } });
        Call(window, "RtcMesaji", "input-test", rtcView, rtcOld);
        Check(keyEvents == 2, "WebRTC native input rejects revoked permission token");
        var rtcNew = Message(new { kind = "data", data = new { tip = "girdi", oturumId = "input-test", izin = "new", eylem = "tus", tus = 66, basili = true, sira = 2 } });
        Call(window, "RtcMesaji", "input-test", rtcView, rtcNew);
        Check(keyEvents == 3, "authorized WebRTC input reaches guarded native sink");
        Call(window, "RtcMesaji", "input-test", rtcView, rtcNew);
        Check(keyEvents == 3, "WebRTC duplicate input rejected at native boundary");
        Call(window, "KontrolTemizle");
        Call(window, "RtcMesaji", "input-test", rtcView, rtcNew);
        Check(keyEvents == 4, "WebRTC revocation releases key and blocks late data");
        using var capture = new CancellationTokenSource();
        var image = new System.Windows.Controls.Image
        {
            Source = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, new byte[4], 4)
        };
        Set(window, "paylasimIptal", capture); Set(window, "uzakEkran", image);
        Call(window, "OturumuTemizle");
        Call(window, "KontrolMesaji", old);
        Check(keyEvents == 4 && !gate.IsActive, "session cleanup blocks all subsequent input");
        Check(capture.IsCancellationRequested && image.Source == null, "real session cleanup cancels capture and drops displayed image reference");
        window.Close();
    }
}
