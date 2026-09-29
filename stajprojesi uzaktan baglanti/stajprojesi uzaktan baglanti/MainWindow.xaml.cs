using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace stajprojesi_uzaktan_baglanti
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            SunucuAdresTextBox.Text = SunucuAyarlari.Yukle();
            Closed += (_, _) => { kapaniyor = true; baglantiIptal?.Cancel(); OturumuTemizle(); socket.Abort(); };
        }

        private System.Net.WebSockets.ClientWebSocket socket = new();
        private string? istemciId;
        private string? oturumId;
        private string? onaylananIstek;
        private bool paylasan, kapaniyor;
        private CancellationTokenSource? paylasimIptal;
        private CancellationTokenSource? baglantiIptal;
        private Window? ekranPenceresi;
        private Image? uzakEkran;

        private void OturumuTemizle()
        {
            KontrolTemizle();
            oturumId = null;
            onaylananIstek = null;
            paylasimIptal?.Cancel();
            paylasimIptal = null;
            var pencere = ekranPenceresi;
            ekranPenceresi = null;
            uzakEkran = null;
            pencere?.Close();
            DurdurButton.Visibility = Visibility.Collapsed;
            DurumText.Text = "Ekran paylasimi durdu.";
        }

        private async void Durdur_Click(object sender, RoutedEventArgs e) => await OturumuDurdur();

        private async Task OturumuDurdur()
        {
            string? id = oturumId;
            OturumuTemizle();
            if (id == null) return;
            try { await MesajGonderAsync(new { tip = "oturum_bitir", oturumId = id }); }
            catch (Exception) { socket.Abort(); }
        }

        private async Task EkranGonder(string id, CancellationTokenSource iptal)
        {
            try
            {
                while (!iptal.IsCancellationRequested && oturumId == id)
                {
                    byte[] jpeg = await Task.Run(EkranYakala.Jpeg, iptal.Token);
                    iptal.Token.ThrowIfCancellationRequested();
                    if (oturumId != id) break;
                    await MesajGonderAsync(new { tip = "ekran", oturumId = id, jpeg = Convert.ToBase64String(jpeg) });
                    await Task.Delay(200, iptal.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (oturumId == id)
                {
                    await OturumuDurdur();
                    if (!kapaniyor) DurumText.Text = "Paylasim durdu: " + ex.Message;
                }
            }
            finally { iptal.Dispose(); }
        }

        private void OturumBaslat(string id, string rol)
        {
            if (rol == "paylasan" && onaylananIstek != id)
                throw new InvalidOperationException("Onaylanmamis ekran paylasimi.");
            OturumuTemizle();
            oturumId = id;
            paylasan = rol == "paylasan";
            DurdurButton.Visibility = Visibility.Visible;
            DurumText.Text = paylasan ? "Birincil ekranin paylasiliyor." : "Karsi ekran bekleniyor...";
            Title = "RemoteDesk | " + (paylasan ? "Ekran paylasiliyor" : "Ekran izleniyor");
            if (paylasan)
            {
                KontrolButton.Visibility = Visibility.Visible;
                paylasimIptal = new CancellationTokenSource();
                _ = EkranGonder(id, paylasimIptal);
            }
            else
            {
                uzakEkran = new Image { Stretch = Stretch.Uniform };
                ekranPenceresi = new Window { Title = "RemoteDesk | Karsi ekran (salt goruntuleme)", Width = 1000, Height = 700, Content = uzakEkran, Background = Brushes.Black, Owner = this };
                ekranPenceresi.Closed += async (_, _) => { if (oturumId == id) await OturumuDurdur(); };
                KontrolOlaylariniBagla();
                ekranPenceresi.Show();
            }
        }
        private readonly System.Threading.SemaphoreSlim gonderimKilidi = new(1, 1);

        private bool BaglantiOnayiAl(string isteyenId)
        {
            var pencere = new Window
            {
                Title = "Baglanti istegi", Owner = this, Width = 460,
                SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(244, 246, 250))
            };
            var alan = new StackPanel { Margin = new Thickness(24) };
            alan.Children.Add(new TextBlock
            {
                Text = "Bu bilgisayara baglanti istegi geldi.",
                FontSize = 18, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            alan.Children.Add(new TextBlock
            {
                Text = "Isteyen istemci: " + isteyenId +
                    "\n\nCevap suresi: 1 dakika.\nKabul edersen birincil ekraninin tamami karsi tarafa gosterilir. Paylasimi istedigin zaman durdurabilirsin. Fare ve klavye kontrolu verilmez.",
                Margin = new Thickness(0, 16, 0, 20), TextWrapping = TextWrapping.Wrap
            });
            var dugmeler = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right
            };
            var reddet = new Button
            {
                Content = "Reddet", Width = 100, Height = 38, IsCancel = true,
                Margin = new Thickness(0, 0, 12, 0)
            };
            var kabul = new Button { Content = "Kabul", Width = 100, Height = 38 };
            reddet.Click += (_, _) => pencere.DialogResult = false;
            kabul.Click += (_, _) => pencere.DialogResult = true;
            dugmeler.Children.Add(reddet);
            dugmeler.Children.Add(kabul);
            alan.Children.Add(dugmeler);
            pencere.Content = alan;
            pencere.Loaded += (_, _) => reddet.Focus();
            return pencere.ShowDialog() == true;
        }

        private async Task MesajGonderAsync(object icerik)
        {
            await gonderimKilidi.WaitAsync();
            try
            {
                byte[] veri = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(icerik);
                using var zamanAsimi = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await socket.SendAsync(new ArraySegment<byte>(veri),
                    System.Net.WebSockets.WebSocketMessageType.Text, true,
                    zamanAsimi.Token);
            }
            finally
            {
                gonderimKilidi.Release();
            }
        }

        private async void PencereYuklendi(object sender, RoutedEventArgs e)
        {
            if (!SunucuBaglanButton.IsEnabled) return;
            if (!Uri.TryCreate(SunucuAdresTextBox.Text.Trim(), UriKind.Absolute, out var adres) ||
                (adres.Scheme != "ws" && adres.Scheme != "wss") || adres.UserInfo.Length > 0 || adres.Fragment.Length > 0)
            {
                MessageBox.Show("Gecerli bir ws:// veya wss:// sunucu adresi gir.");
                return;
            }
            if (adres.Scheme == "ws" && (YerelTestCheckBox.IsChecked != true || !YerelAdres(adres.Host)))
            {
                MessageBox.Show("Sifresiz baglanti sadece yerel/ozel IP adreslerinde ve yerel test secenegi isaretliyken kullanilabilir.");
                return;
            }
            socket.Dispose();
            socket = new System.Net.WebSockets.ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(20);
            SunucuBaglanButton.IsEnabled = false;
            SunucuAdresTextBox.IsEnabled = false;
            YerelTestCheckBox.IsEnabled = false;
            KendiKodumTextBlock.Text = "Henuz kod olusturulmadi";
            SunucuDurumText.Text = "Baglaniliyor...";
            using var baglantiZamanAsimi = new CancellationTokenSource(
                TimeSpan.FromSeconds(RenderSunucu.RenderAdresi(adres) ? 120 : 10));
            baglantiIptal = baglantiZamanAsimi;
            try
            {
                if (RenderSunucu.RenderAdresi(adres))
                {
                    SunucuDurumText.Text = "Sunucu hazirlaniyor; ilk baglanti 1-2 dakika surebilir...";
                    await RenderSunucu.UyandirAsync(adres, baglantiZamanAsimi.Token);
                }
                await socket.ConnectAsync(
                    adres, baglantiZamanAsimi.Token);
                baglantiZamanAsimi.CancelAfter(Timeout.InfiniteTimeSpan);

                var buffer = new byte[4096];
                using var mesaj = new System.IO.MemoryStream();

                while (socket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    mesaj.SetLength(0);
                    System.Net.WebSockets.WebSocketReceiveResult sonuc;

                    do
                    {
                        sonuc = await socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            System.Threading.CancellationToken.None);

                        if (sonuc.MessageType ==
                            System.Net.WebSockets.WebSocketMessageType.Close)
                        {
                            istemciId = null;
                            await socket.CloseOutputAsync(
                                System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                                "Baglanti kapatildi.",
                                System.Threading.CancellationToken.None);
                            MessageBox.Show("Sunucu baglantiyi kapatti.");
                            return;
                        }

                        if (sonuc.MessageType != System.Net.WebSockets.WebSocketMessageType.Text ||
                            mesaj.Length + sonuc.Count > 2 * 1024 * 1024)
                        {
                            socket.Abort();
                            throw new InvalidOperationException("Gecersiz sunucu mesaji.");
                        }

                        mesaj.Write(buffer, 0, sonuc.Count);
                    }
                    while (!sonuc.EndOfMessage);

                    using var belge =
                        System.Text.Json.JsonDocument.Parse(mesaj.ToArray());

                    string? tip = belge.RootElement.GetProperty("tip").GetString();

                    if (tip == "kimlik")
                    {
                        istemciId = belge.RootElement
                            .GetProperty("istemciId").GetString();

                        SunucuDurumText.Text = "Sunucuya bagli";
                        DurumText.Text = "Baglantiya hazir. Ekran paylasimi icin onay gerekir.";
                        if (!SunucuAyarlari.Kaydet(adres))
                            DurumText.Text += " Sunucu adresi kaydedilemedi; sonraki acilista yeniden gir.";
                    }
                    else if (tip == "kod_olusturuldu")
                    {
                        KendiKodumTextBlock.Text = belge.RootElement
                            .GetProperty("kod").GetString();
                    }
                    else if (tip == "baglanti_istegi")
                    {
                        string? isteyenId = belge.RootElement.GetProperty("isteyenId").GetString();
                        string? istekId = belge.RootElement.GetProperty("istekId").GetString();
                        bool kabul = BaglantiOnayiAl(isteyenId ?? "");
                        onaylananIstek = kabul ? istekId : null;
                        await MesajGonderAsync(new { tip = "baglanti_cevabi", istekId, kabul });
                    }
                    else if (tip == "kontrol_izni" || tip == "girdi")
                    {
                        KontrolMesaji(belge.RootElement);
                    }
                    else if (tip == "oturum_basladi")
                    {
                        OturumBaslat(belge.RootElement.GetProperty("oturumId").GetString()!, belge.RootElement.GetProperty("rol").GetString()!);
                    }
                    else if (tip == "oturum_bitti")
                    {
                        if (oturumId == belge.RootElement.GetProperty("oturumId").GetString()) OturumuTemizle();
                    }
                    else if (tip == "ekran" && !paylasan && uzakEkran != null && oturumId == belge.RootElement.GetProperty("oturumId").GetString())
                    {
                        using var veri = new System.IO.MemoryStream(Convert.FromBase64String(belge.RootElement.GetProperty("jpeg").GetString()!));
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = veri;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        uzakEkran.Source = bitmap;
                        DurumText.Text = "Karsi ekran goruntuleniyor.";
                    }
                    else if (tip == "istek_gonderildi")
                    {
                        Title = "RemoteDesk | Cevap bekleniyor (1 dakika)";
                    }
                    else if (tip == "baglanti_sonucu")
                    {
                        Title = "RemoteDesk | Uzaktan Baglanti";
                        string? durum = belge.RootElement.GetProperty("durum").GetString();
                        string sonucMesaji = durum switch
                        {
                            "kabul" => "Karsi taraf istegi kabul etti. Ekran paylasimi henuz baslamadi.",
                            "ret" => "Karsi taraf istegi reddetti.",
                            _ => "Istegin suresi doldu. Tekrar istek gonder."
                        };
                        MessageBox.Show(this, sonucMesaji, "Baglanti cevabi");
                    }
                    else if (tip == "bilgi")
                    {
                        MessageBox.Show(this,
                            belge.RootElement.GetProperty("mesaj").GetString() ?? "",
                            "Sunucu mesaji");
                    }
                }
            }
            catch (Exception ex)
            {
                OturumuTemizle();
                if (!kapaniyor) MessageBox.Show("Baglanti hatasi: " + ex.Message);
            }
            finally { baglantiIptal = null; istemciId = null; OturumuTemizle(); socket.Abort(); SunucuDurumText.Text = "Sunucu bagli degil"; SunucuBaglanButton.IsEnabled = true; SunucuAdresTextBox.IsEnabled = true; YerelTestCheckBox.IsEnabled = true; }
        }

        private static bool YerelAdres(string host)
        {
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (!System.Net.IPAddress.TryParse(host, out var ip)) return false;
            if (System.Net.IPAddress.IsLoopback(ip)) return true;
            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
            var b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31);
        }


        private async void BaglantiAl_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(istemciId) ||
                socket.State != System.Net.WebSockets.WebSocketState.Open)
            {
                MessageBox.Show("Sunucu baglantisi hazir degil.");
                return;
            }

            try
            {
                await MesajGonderAsync(new { tip = "kod_iste" });
            }
            catch (Exception)
            {
                MessageBox.Show("Kod istegi gonderilemedi.");
            }
        }

        private async void Baglan_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(istemciId) ||
                socket.State != System.Net.WebSockets.WebSocketState.Open)
            {
                MessageBox.Show("Sunucu baglantisi hazir degil.");
                return;
            }

            string kod = BaglantiKoduTextBox.Text.Trim();

            if (kod.Length != 8 || kod.Any(c => c < '0' || c > '9'))
            {
                MessageBox.Show("8 haneli sayisal baglanti kodunu gir.");
                return;
            }

            try
            {
                await MesajGonderAsync(new { tip = "baglan", kod });
            }
            catch (Exception)
            {
                MessageBox.Show("Istek gonderilemedi. Sunucu baglantisini kontrol et.");
            }
        }
    }
}

