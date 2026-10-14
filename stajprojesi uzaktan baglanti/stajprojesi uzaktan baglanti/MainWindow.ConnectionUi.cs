using System.Windows;
using System.Windows.Threading;

namespace stajprojesi_uzaktan_baglanti;

public partial class MainWindow
{
    private readonly DispatcherTimer kodSayaci = new() { Interval = TimeSpan.FromSeconds(1) };
    private long? kodBitis;
    private string baglananBilgisi = "";
    private bool kareOnayiDestegi;
    // Acknowledgements measure delivered FPS; they never gate frame sending.
    private long bekleyenKareSayisi;
    private double yakalamaMs, gonderimMs, kareTurMs;
    private int sonKareBayt, sonAgBayt;
    private double paketHazirlamaMs, agYazmaMs;
    private string etkinGoruntu = "";
    private readonly System.Diagnostics.Stopwatch fpsOlcum = new();
    private int fpsKare;

    private void UygulamaYuklendi(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SunucuAdresTextBox.Text)) PencereYuklendi(sender, e);
    }

    private void KodSayaciniTemizle()
    {
        kodSayaci.Stop(); kodBitis = null;
        KodSureText.Text = "";
        KendiKodumTextBlock.Text = "Henuz kod olusturulmadi";
    }

    private void KodSuresiniGuncelle()
    {
        if (kodBitis == null)
        {
            KodSureText.Text = "Sure bilgisi icin sunucuyu guncelleyin.";
            return;
        }
        int saniye = (int)Math.Max(0, Math.Ceiling((kodBitis.Value - Environment.TickCount64) / 1000.0));
        KodSureText.Text = $"Kalan sure: {saniye / 60:00}:{saniye % 60:00}";
        if (saniye == 0)
        {
            kodSayaci.Stop();
            KendiKodumTextBlock.Text = "Kodun suresi doldu";
            KodSureText.Text = "Yeni kod almak icin Baglanti Al'a basin.";
        }
    }

    private void FpsGuncelle(int? hedef = null)
    {
        fpsKare++;
        if (fpsOlcum.Elapsed.TotalSeconds < 1) return;
        FpsText.Text = $"Gercek hiz: {fpsKare / fpsOlcum.Elapsed.TotalSeconds:0.0} FPS" +
            (hedef.HasValue ? $" / Hedef: {hedef} FPS" : "") +
            (hedef.HasValue ? $"\nYakalama: {yakalamaMs:0} ms | Gonderim: {gonderimMs:0} ms | Kare onayi: {kareTurMs:0} ms | JPEG: {sonKareBayt / 1024.0:0} KB\nPaket: {paketHazirlamaMs:0} ms | Ag yazimi: {agYazmaMs:0} ms | Ag verisi: {sonAgBayt / 1024.0:0} KB | {etkinGoruntu}" : "");
        fpsKare = 0; fpsOlcum.Restart();
    }
}
