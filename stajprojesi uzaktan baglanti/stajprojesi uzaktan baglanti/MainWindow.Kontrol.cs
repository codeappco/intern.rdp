using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace stajprojesi_uzaktan_baglanti;

public partial class MainWindow
{
    private string kontrolIzni = "";
    private readonly UzakGirdi uzakGirdi = new();
    private bool girdiAktif;
    private long sonHareket;
    private bool hareketGonderiliyor;

    private void KontrolTemizle()
    {
        kontrolIzni = "";
        girdiAktif = false;
        uzakGirdi.Release();
        KontrolButton.Visibility = Visibility.Collapsed;
        KontrolButton.Content = "Fare ve klavye kontrolune izin ver";
    }

    private async void Kontrol_Click(object sender, RoutedEventArgs e)
    {
        if (!paylasan || oturumId == null) return;
        // Yerel izin hemen kaldirilir; yolda kalan eski girdiler uygulanmaz.
        kontrolIzni = kontrolIzni.Length == 0 ? Guid.NewGuid().ToString() : "";
        uzakGirdi.Release();
        KontrolButton.Content = kontrolIzni.Length > 0 ? "Kontrol iznini kaldir" : "Fare ve klavye kontrolune izin ver";
        DurumText.Text = kontrolIzni.Length > 0 ? "Ekranin paylasiliyor. Fare ve klavye kontrolu acik." : "Ekranin paylasiliyor. Kontrol izni kapali.";
        try { await MesajGonderAsync(new { tip = "kontrol_izni", oturumId, izin = kontrolIzni }); }
        catch (Exception) { await OturumuDurdur(); }
    }

    private async Task GirdiGonder(string eylem, double x = 0, double y = 0, int dugme = 0, bool basili = false, int tus = 0, int delta = 0)
    {
        if (paylasan || oturumId == null || kontrolIzni.Length == 0) return;
        try { await MesajGonderAsync(new { tip = "girdi", oturumId, izin = kontrolIzni, eylem, x, y, dugme, basili, tus, delta }); }
        catch (Exception) { await OturumuDurdur(); }
    }

    private bool EkranNoktasi(MouseEventArgs e, out double x, out double y)
    {
        x = y = 0;
        if (uzakEkran?.Source == null || uzakEkran.ActualWidth <= 0 || uzakEkran.ActualHeight <= 0) return false;
        double oran = Math.Min(uzakEkran.ActualWidth / uzakEkran.Source.Width, uzakEkran.ActualHeight / uzakEkran.Source.Height);
        double w = uzakEkran.Source.Width * oran, h = uzakEkran.Source.Height * oran;
        var p = e.GetPosition(uzakEkran);
        x = (p.X - (uzakEkran.ActualWidth - w) / 2) / w;
        y = (p.Y - (uzakEkran.ActualHeight - h) / 2) / h;
        return x >= 0 && x <= 1 && y >= 0 && y <= 1;
    }

    private void KontrolOlaylariniBagla()
    {
        if (uzakEkran == null || ekranPenceresi == null) return;
        uzakEkran.Focusable = true;
        uzakEkran.MouseDown += async (_, e) =>
        {
            if (UzakGirdi.IsInjected) { e.Handled = true; return; }
            if (kontrolIzni.Length == 0 || !EkranNoktasi(e, out var x, out var y)) return;
            int button = e.ChangedButton == MouseButton.Left ? 0 : e.ChangedButton == MouseButton.Right ? 1 : e.ChangedButton == MouseButton.Middle ? 2 : -1;
            if (button < 0) return;
            girdiAktif = true;
            uzakEkran.Focus();
            e.Handled = true;
            await GirdiGonder("dugme", x, y, button, true);
        };
        uzakEkran.MouseUp += async (_, e) =>
        {
            if (UzakGirdi.IsInjected) { e.Handled = true; return; }
            if (!girdiAktif) return;
            if (!EkranNoktasi(e, out var x, out var y)) { await GirdiGonder("birak"); return; }
            int button = e.ChangedButton == MouseButton.Left ? 0 : e.ChangedButton == MouseButton.Right ? 1 : e.ChangedButton == MouseButton.Middle ? 2 : -1;
            if (button >= 0) { e.Handled = true; await GirdiGonder("dugme", x, y, button); }
        };
        uzakEkran.MouseMove += async (_, e) =>
        {
            long simdi = Environment.TickCount64;
            if (UzakGirdi.IsInjected) { e.Handled = true; return; }
            if (!girdiAktif || hareketGonderiliyor || simdi - sonHareket < 40 || !EkranNoktasi(e, out var x, out var y)) return;
            sonHareket = simdi;
            hareketGonderiliyor = true;
            try { await GirdiGonder("hareket", x, y); }
            finally { hareketGonderiliyor = false; }
        };
        uzakEkran.MouseWheel += async (_, e) =>
        {
            if (UzakGirdi.IsInjected) { e.Handled = true; return; }
            if (!girdiAktif || !EkranNoktasi(e, out var x, out var y)) return;
            e.Handled = true;
            await GirdiGonder("tekerlek", x, y, delta: Math.Clamp(e.Delta, -1200, 1200));
        };
        uzakEkran.MouseLeave += async (_, _) => { girdiAktif = false; await GirdiGonder("birak"); };
        ekranPenceresi.Deactivated += async (_, _) => { girdiAktif = false; await GirdiGonder("birak"); };
        ekranPenceresi.PreviewKeyDown += async (_, e) =>
        {
            if (UzakGirdi.IsInjected) { e.Handled = true; return; }
            if (!girdiAktif || kontrolIzni.Length == 0) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            e.Handled = true;
            if (key == Key.Escape) { girdiAktif = false; await GirdiGonder("birak"); return; }
            await GirdiGonder("tus", tus: KeyInterop.VirtualKeyFromKey(key), basili: true);
        };
        ekranPenceresi.PreviewKeyUp += async (_, e) =>
        {
            if (UzakGirdi.IsInjected) { e.Handled = true; return; }
            if (!girdiAktif || kontrolIzni.Length == 0) return;
            e.Handled = true;
            await GirdiGonder("tus", tus: KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key));
        };
    }

    private void KontrolMesaji(JsonElement veri)
    {
        if (oturumId == null || veri.GetProperty("oturumId").GetString() != oturumId) return;
        if (veri.GetProperty("tip").GetString() == "kontrol_izni" && !paylasan)
        {
            kontrolIzni = veri.GetProperty("izin").GetString() ?? "";
            girdiAktif = false;
            if (ekranPenceresi != null) ekranPenceresi.Title = kontrolIzni.Length > 0
                ? "RemoteDesk | Kontrol acik: goruntuye tikla. Esc: kontrolu duraklat"
                : "RemoteDesk | Karsi ekran (salt goruntuleme)";
        }
        else if (veri.GetProperty("tip").GetString() == "girdi" && paylasan && kontrolIzni.Length > 0 && veri.GetProperty("izin").GetString() == kontrolIzni)
        {
            try { uzakGirdi.Apply(veri); }
            catch (Exception) { Kontrol_Click(this, new RoutedEventArgs()); }
        }
    }
}

