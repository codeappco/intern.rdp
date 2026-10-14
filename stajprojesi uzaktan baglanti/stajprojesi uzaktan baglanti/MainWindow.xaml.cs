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
            SunucuAyariniYukle();
            AyarlariYukle();
            kodSayaci.Tick += (_, _) => KodSuresiniGuncelle();
            Closed += (_, _) => { kapaniyor = true; kodSayaci.Stop(); pingIptal?.Cancel(); OturumuTemizle(); socket.Abort(); };
        }

        private void SunucuAyariniYukle()
        {
            // Single-file extraction may put assemblies in a temporary directory.
            // Configuration belongs beside the executable the user launched.
            string klasor = System.IO.Path.GetDirectoryName(Environment.ProcessPath)
                ?? AppContext.BaseDirectory;
            string dosya = System.IO.Path.Combine(klasor, "sunucu.json");
            SunucuAdresTextBox.Text = "";
            try
            {
                using var belge = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(dosya));
                string? deger = belge.RootElement.GetProperty("SunucuAdresi").GetString();
                if (!Uri.TryCreate(deger, UriKind.Absolute, out var adres) ||
                    (adres.Scheme != "wss" && adres.Scheme != "ws") ||
                    adres.UserInfo.Length > 0 || adres.Fragment.Length > 0)
                    throw new FormatException("Gecersiz sunucu adresi.");
                SunucuAdresTextBox.Text = adres.AbsoluteUri;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException ||
                ex is System.Text.Json.JsonException || ex is KeyNotFoundException ||
                ex is InvalidOperationException || ex is FormatException)
            {
                DurumText.Text = "sunucu.json okunamadi veya gecersiz. Sunucu adresini elle girin.";
            }
        }

        private ResponsiveWebSocket socket = new();
        private string? istemciId;
        private string? oturumId;
        private string? onaylananIstek;
        private bool paylasan, kapaniyor;
        private CancellationTokenSource? pingIptal;
        private long sonPingGonderim;
        private CancellationTokenSource? paylasimIptal;
        private Window? ekranPenceresi;
        private Image? uzakEkran;

        private void OturumuTemizle()
        {
            RtcTemizle();
            bekleyenKareSayisi = 0; kareOnayiDestegi = false; kareTurMs = 0;
            BaglantiKartlari.Visibility = Visibility.Visible;
            AktifOturumPanel.Visibility = Visibility.Collapsed;
            Title = "RemoteDesk | Uzaktan Baglanti";
            secureTimeout?.Cancel(); secureTimeout?.Dispose(); secureTimeout = null;
            secureSession?.Dispose(); secureSession = null;
            peerVerification?.Close(); peerVerification = null;
            secureActivated = false;
            var dialog = verificationDialog; verificationDialog = null; dialog?.Close();
            KontrolTemizle();
            oturumId = null;
            onaylananIstek = null;
            paylasimIptal?.Cancel();
            paylasimIptal = null;
            var pencere = ekranPenceresi;
            ekranPenceresi = null;
            if (uzakEkran != null) uzakEkran.Source = null;
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
            var profile = new StreamImageProfile();
            try
            {
                while (!iptal.IsCancellationRequested && oturumId == id)
                {
                    var frameStart = System.Diagnostics.Stopwatch.StartNew();
                    var settings = preferences;
                    profile.Configure(settings.Height, settings.AdaptiveImage);
                    int captureHeight = profile.Height, captureQuality = profile.Quality;
                    etkinGoruntu = $"{captureHeight}p / JPEG kalite {captureQuality}";
                    byte[] jpeg = await Task.Run(() => EkranYakala.Jpeg(captureHeight, captureQuality), iptal.Token);
                    yakalamaMs = frameStart.Elapsed.TotalMilliseconds;
                    sonKareBayt = jpeg.Length;
                    try
                    {
                    iptal.Token.ThrowIfCancellationRequested();
                    if (oturumId != id) break;
                    if (kareOnayiDestegi) bekleyenKareSayisi++;
                    var sendWatch = System.Diagnostics.Stopwatch.StartNew();
                    await SecureSend(new { tip = "ekran", oturumId = id, gonderimZamani = Environment.TickCount64, jpeg = Convert.ToBase64String(jpeg) });
                    gonderimMs = sendWatch.Elapsed.TotalMilliseconds;
                    profile.Observe(jpeg.Length, gonderimMs, yakalamaMs, settings.Fps);
                    if (!kareOnayiDestegi) FpsGuncelle(settings.Fps);
                    }
                    finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(jpeg); }
                    var remaining = TimeSpan.FromSeconds(1.0 / settings.Fps) - frameStart.Elapsed;
                    if (remaining > TimeSpan.Zero) await Task.Delay(remaining, iptal.Token);
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

        private async Task PingDongusu(CancellationToken token)
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
                while (await timer.WaitForNextTickAsync(token))
                {
                    long zaman = Environment.TickCount64;
                    sonPingGonderim = zaman;
                    await MesajGonderAsync(new { tip = "ping", zaman });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }

        private void OturumBaslat(string id, string rol)
        {
            if (!secureStarted || oturumId != id) throw new InvalidOperationException("Guvenli oturum hazir degil.");
            BaglantiKartlari.Visibility = Visibility.Collapsed;
            AktifOturumPanel.Visibility = Visibility.Visible;
            BaglananText.Text = paylasan ? "Baglanan (gorunen ad, dogrulanmamis): " + baglananBilgisi : "Karsi bilgisayarin ekrani izleniyor.";
            fpsOlcum.Restart(); fpsKare = 0;
            KodSayaciniTemizle();
            DurdurButton.Visibility = Visibility.Visible;
            DurumText.Text = paylasan ? "Birincil ekranin paylasiliyor." : "Karsi ekran bekleniyor...";
            Title = "RemoteDesk | " + (paylasan ? "Ekran paylasiliyor" : "Ekran izleniyor");
            rtcSession = (paylasan ? localVideoTransport : peerVideoTransport) == "webrtc";
            if (rtcSession)
            {
                rtcInitialization = RtcBaslat(id);
                return;
            }
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

        private bool BaglantiOnayiAl(string isteyenId, string isteyenAd)
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
                Text = "Gorunen ad (dogrulanmamis): " + isteyenAd + "\nIsteyen istemci: " + isteyenId +
                    "\n\nCevap suresi: 1 dakika.\nKabul edince sifreli baglanti kurulur ve birincil ekraninin tamami karsi tarafa gosterilir. Ayarlarda etkinse guvenlik kodu da dogrulanir. Paylasimi istedigin zaman durdurabilirsin. Fare ve klavye kontrolu verilmez.",
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
            socket = new ResponsiveWebSocket();
            SunucuBaglanButton.IsEnabled = false;
            SunucuAdresTextBox.IsEnabled = false;
            YerelTestCheckBox.IsEnabled = false;
            KendiKodumTextBlock.Text = "Henuz kod olusturulmadi";
            SunucuDurumText.Text = "Baglaniliyor...";
            SunucuDurumText.Foreground = Brushes.DarkOrange;
            KodSayaciniTemizle();
            try
            {
                using var baglantiZamanAsimi = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await socket.ConnectAsync(
                    adres, baglantiZamanAsimi.Token);

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
                        SunucuDurumText.Foreground = Brushes.ForestGreen;
                        DurumText.Text = "Baglantiya hazir. Ekran paylasimi icin onay gerekir.";
                        pingIptal = new CancellationTokenSource();
                        _ = PingDongusu(pingIptal.Token);
                    }
                    else if (tip == "pong")
                    {
                        long gonderim = belge.RootElement.GetProperty("zaman").GetInt64();
                        long simdi = Environment.TickCount64;
                        if (gonderim <= simdi && simdi - gonderim < 60000)
                        {
                            int ms = (int)(simdi - gonderim);
                            PingText.Text = $"Ping: gonderilen {ms} ms / alinan {ms} ms";
                        }
                    }
                    else if (tip == "kod_olusturuldu")
                    {
                        KendiKodumTextBlock.Text = belge.RootElement
                            .GetProperty("kod").GetString();
                        kodBitis = belge.RootElement.TryGetProperty("gecerlilikSaniye", out var sure) ? Environment.TickCount64 + sure.GetInt32() * 1000L : null;
                        KodSuresiniGuncelle();
                        if (kodBitis != null) kodSayaci.Start();
                    }
                    else if (tip == "baglanti_istegi")
                    {
                        string? isteyenId = belge.RootElement.GetProperty("isteyenId").GetString();
                        string? istekId = belge.RootElement.GetProperty("istekId").GetString();
                        string ad = belge.RootElement.TryGetProperty("isteyenAd", out var adAlani) && adAlani.ValueKind == System.Text.Json.JsonValueKind.String ? adAlani.GetString() ?? "" : "";
                        var kimlik = new UserSettings { DisplayName = ad.Length == 0 ? "RemoteDesk-" + (isteyenId ?? "bilinmiyor")[..Math.Min(8, (isteyenId ?? "bilinmiyor").Length)] : ad };
                        kimlik.Normalize();
                        bool kabul = BaglantiOnayiAl(isteyenId ?? "", kimlik.DisplayName);
                        onaylananIstek = kabul ? istekId : null;
                        if (kabul) baglananBilgisi = kimlik.DisplayName + "\nIstemci: " + isteyenId;
                        await MesajGonderAsync(new { tip = "baglanti_cevabi", istekId, kabul });
                    }
                    else if (tip == "kontrol_izni" || tip == "girdi")
                    {
                        throw new System.Security.Cryptography.CryptographicException("Sifresiz kontrol mesaji reddedildi.");
                    }
                    else if (tip == "oturum_basladi")
                    {
                        await SecureStart(belge.RootElement.GetProperty("oturumId").GetString()!, belge.RootElement.GetProperty("rol").GetString()!);
                    }
                    else if (tip == "oturum_bitti")
                    {
                        if (oturumId == belge.RootElement.GetProperty("oturumId").GetString()) OturumuTemizle();
                    }
                    else if (tip == "e2ee_anahtar" || tip == "sifreli")
                    {
                        await SecureMessage(belge.RootElement);
                    }
                    else if (tip == "ekran")
                    {
                        throw new System.Security.Cryptography.CryptographicException("Sifresiz ekran reddedildi.");
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
                        DurumText.Text = sonucMesaji;
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
            finally { pingIptal?.Cancel(); pingIptal?.Dispose(); pingIptal = null; PingText.Text = "Ping: -- / -- ms"; istemciId = null; OturumuTemizle(); socket.Abort(); SunucuDurumText.Text = "Sunucu bagli degil"; SunucuDurumText.Foreground = Brushes.Firebrick; KodSayaciniTemizle(); SunucuBaglanButton.IsEnabled = true; SunucuAdresTextBox.IsEnabled = true; YerelTestCheckBox.IsEnabled = true; }
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
                await MesajGonderAsync(new { tip = "baglan", kod, ad = preferences.DisplayName });
            }
            catch (Exception)
            {
                MessageBox.Show("Istek gonderilemedi. Sunucu baglantisini kontrol et.");
            }
        }
    }
}

