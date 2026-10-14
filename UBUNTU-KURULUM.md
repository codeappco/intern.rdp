# Ubuntu sunucu kurulumu — VirtualBox, Docker ve internetten test

Bu rehber, Windows bilgisayarda VirtualBox içine Ubuntu Server kurup RemoteDesk sunucusunu çalıştırmak için izlediğimiz adımları anlatır. Son aşamada Cloudflare geçici tüneliyle farklı internetlerden bağlantı denenir.

**Nerede komut yazacağınız:** “Windows PowerShell” komutlarını Windows'ta; “Ubuntu / SSH” komutlarını Ubuntu terminalinde veya Ubuntu'ya SSH ile bağlandığınız pencerede çalıştırın. Komutları sırayla uygulayın, hata varsa sonraki adıma geçmeyin.

## 1. Ubuntu ISO dosyasını indirin

[Ubuntu 24.04 LTS](https://releases.ubuntu.com/releases/24.04/) sayfasından **Server install image — 64-bit PC (AMD64)** ISO dosyasını indirin. Bu rehberde Ubuntu Server 24.04.5 x64 kullanıldı.

ISO'yu USB'ye yazmayın; VirtualBox'a bağlayacağız. Ubuntu Server masaüstü arayüzü içermez.

## 2. VirtualBox'ta sanal makine oluşturun

VirtualBox → **Yeni / New**:

| Ayar | Değer |
| --- | --- |
| İsim | RemoteDesk-Ubuntu |
| ISO | İndirdiğiniz Ubuntu Server ISO dosyası |
| Sistem | Linux / Ubuntu 64-bit |
| Otomatik kurulum | Skip Unattended Installation seçili |
| RAM | Başlangıç için 4 GB; toplam 8 GB RAM varsa 2 GB ile başlayabilirsiniz |
| İşlemci | 2 sanal işlemci; ana bilgisayara da kaynak bırakın |
| Disk | Yeni VDI, dinamik ayrılan, 40 GB |

Bu denemede 30 GB sanal disk kullanıldı. Docker imajları zamanla yer kapladığından yeni kurulum için 40 GB ayırabilirsiniz.

Makine kapalıyken **Ayarlar → Ağ → Bağdaştırıcı 1**:

- Ağ bağdaştırıcısını etkinleştirin.
- **Köprü Bağdaştırıcısı / Bridged Adapter** seçin.
- Wi-Fi kullanıyorsanız gerçek Wi-Fi kartınızı, kablolu bağlantıda gerçek Ethernet kartınızı seçin.
- VPN, Host-Only veya Hyper-V sanal adaptörünü seçmeyin.
- **Kablo bağlı** işaretli kalsın.

Bu ayar Ubuntu'nun yerel ağdan ayrı bir IP almasını sağlar. İnternetten erişimi tek başına açmaz.

## 3. Ubuntu kurulumunu tamamlayın

Sanal makineyi başlatıp **Try or Install Ubuntu Server** seçin.

1. Dil ve klavye düzenini seçin.
2. Kurulum türünde normal **Ubuntu Server** seçin; minimized seçmeyin.
3. Üçüncü taraf sürücü seçeneğini boş bırakın.
4. Ağ ekranında DHCP ile IP alınmasını bekleyin. Bu kurulumda `192.168.1.13` alındı; sizin adresiniz farklı olabilir.
5. Proxy alanını boş bırakın; mirror adresini varsayılanda bırakın.
6. Depolamada **Use an entire disk** seçin. Hedefin oluşturduğunuz **VBOX_HARDDISK** olduğunu ve boyutunu kontrol edin.
7. Bu basit test kurulumu için LVM işaretini kaldırın, disk şifrelemesini boş bırakın.
8. Disk özetinde **Done**, biçimlendirme onayında **Continue** seçin. Seçilen sanal diskin içeriği silinir.
9. Kullanıcı bilgilerini doldurun:

| Alan | Örnek |
| --- | --- |
| Your name | Kendi adınız |
| Your server's name | remotedesk-server |
| Pick a username | sunucu |
| Password | Kendinizin belirlediği güçlü parola |

10. Ubuntu Pro adımını şimdilik atlayın.
11. **Install OpenSSH server** seçeneğini işaretleyin. **Allow password authentication over SSH** seçili kalsın. SSH anahtarı içe aktarma adımını atlayın.
12. Featured server snaps ekranında hiçbir paket seçmeden **Done** deyin.
13. Kurulum tamamlanınca **Reboot Now** seçin.

**Klavye kullanımı:** Alanlar arasında Tab/yön tuşları, seçimlerde Space, onayda Enter kullanın. Ekrana tıklamak klavyeyi sanal makineye aktarır; sağ Ctrl Windows'a geri bırakır.

### “Please remove the installation medium” ekranı

VirtualBox → **Aygıtlar → Optik Sürücüler → Diski sanal sürücüden çıkar**. Gerekirse “Zorla çıkar” seçin; ardından Ubuntu ekranında Enter'a basın.

Çıkarma seçeneği griyse ve ISO seçili değilse disk zaten ayrılmış olabilir. Enter'ı deneyin. Kurulum tamamlanmış olmasına rağmen burada kalırsa ISO'nun çıkarıldığını kontrol edip **Makine → Sıfırla** ile yeniden başlatın. Bu öneri yalnızca kurulum bittikten sonraki bu ekran içindir.

## 4. Giriş yapın ve IP adresini öğrenin

Ubuntu'da `login:` satırına kullanıcı adınızı, ardından parolanızı girin. Parola yazılırken karakter görünmez.

**Ubuntu:**

```bash
hostname -I
```

Yerel IPv4 adresini not edin. Aşağıdaki örnekler `192.168.1.13` ve `sunucu` kullanır; kendi değerlerinizle değiştirin.

## 5. Windows PowerShell'den Ubuntu'ya bağlanın

**Windows PowerShell:**

```powershell
ssh sunucu@192.168.1.13
```

İlk bağlantıda sunucu anahtarı onayı çıkar. Doğru sanal makineye bağlandığınızı doğrulayıp kabul edin; isterseniz Ubuntu konsolunda `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub` çıktısını gösterilen parmak iziyle karşılaştırın. Ardından Ubuntu parolanızı girin.

Satır başında `sunucu@remotedesk-server:~$` görünüyorsa artık komutlar Ubuntu'da çalışır. Windows Terminal'de **Ctrl+V** veya sağ tıkla yapıştırabilirsiniz. VirtualBox konsoluna yapıştırmak zorunda değilsiniz.

## 6. Ubuntu'yu güncelleyin

**Ubuntu / SSH — her komutun bitmesini bekleyin:**

```bash
sudo apt update
sudo apt upgrade -y
sudo apt install -y git curl ca-certificates
sudo reboot
```

Yeniden başlatma SSH bağlantısını kapatır. Ubuntu açılınca Windows PowerShell'den yeniden bağlanın:

```powershell
ssh sunucu@192.168.1.13
```

Bağlanmazsa Ubuntu konsolundan `hostname -I` ile IP'nin değişmediğini kontrol edin. Bu rehber için 24.04'ten başka bir Ubuntu sürümüne yükseltmeniz gerekmez.

## 7. Docker ve Compose kurun

Aşağıdaki depo ayarları **Ubuntu 24.04 (noble), amd64** içindir. Önce kontrol edin:

```bash
cat /etc/os-release
dpkg --print-architecture
```

Başka sürüm veya mimari varsa [Docker'ın resmî Ubuntu rehberini](https://docs.docker.com/engine/install/ubuntu/) kullanın.

**Ubuntu / SSH:**

```bash
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
```

Bu komutların çıktı vermemesi normaldir. Dosyayı kontrol edin:

```bash
ls -l /etc/apt/keyrings/docker.asc
```

Ardından aşağıdaki bloğu, son `EOF` satırı dahil, tek seferde yapıştırın:

```bash
sudo tee /etc/apt/sources.list.d/docker.sources > /dev/null <<EOF
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: noble
Components: stable
Architectures: amd64
Signed-By: /etc/apt/keyrings/docker.asc
EOF
```

Kurun ve başlatın:

```bash
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo systemctl enable --now docker
```

Kontrol edin:

```bash
sudo docker version
sudo docker compose version
sudo docker run --rm hello-world
```

Docker sürümünde **Client** ve **Server**, testte **Hello from Docker!** görünmelidir.

## 8. Projeyi GitHub'dan indirin

Bu test sırasında güncel kod **new-branch** dalındaydı; main'e birleştirme yapılmadı.

**Ubuntu / SSH:**

```bash
git clone --branch new-branch --single-branch https://github.com/codeappco/intern.rdp.git
cd intern.rdp
git branch --show-current
ls deploy
```

Dal adı `new-branch`, dosya listesinde `Dockerfile` görünmeli.

Klasör zaten varsa tekrar klonlamayın. Doğru dalda olduğunuzu kontrol edip yerel değişiklik yoksa güncelleyin:

```bash
cd ~/intern.rdp
git branch --show-current
git status --short
git pull --ff-only
```

Kod ileride main'e birleştirilirse yeni kurulumda klonlama komutundaki dalı main olarak değiştirin.

## 9. Sunucuyu Docker'da çalıştırın

**Ubuntu / SSH — proje klasöründe:**

```bash
cd ~/intern.rdp
sudo docker build -f deploy/Dockerfile -t remotedesk-server .
```

Son noktayı unutmayın. İlk derlemede .NET imajları indirilir; birkaç dakika sürebilir. Hatasız tamamlanınca:

```bash
sudo docker run -d --name remotedesk-server --restart unless-stopped -p 8080:8080 remotedesk-server
```

Bu komutu yalnızca container ilk oluşturulurken çalıştırın. Aynı isim zaten varsa yeniden `run` yerine `sudo docker start remotedesk-server` kullanın.

Kontrol:

```bash
sudo docker ps
curl -i http://127.0.0.1:8080/ws
```

- Container **Up** görünmeli.
- Normal HTTP isteğine **400 Bad Request** dönmesi beklenir; `/ws` WebSocket bağlantısı ister.
- Bu kontrol gerçek ekran paylaşımını henüz doğrulamaz.

Hata varsa:

```bash
sudo docker logs --tail=100 remotedesk-server
```

Bu port eşlemesi sunucuyu Ubuntu'nun ağ arayüzlerinde 8080 üzerinden erişilebilir yapar. Yerel ağ testi adresi `ws://192.168.1.13:8080/ws` olur; bu adres için istemcide şifresiz yerel test seçeneği gerekir. Aşağıdaki HTTPS tünelinde bu seçeneği kullanmayacağız.

## 10. CGNAT varsa geçici internet tüneli açın

Bu denemede modem WAN adresi `100.64.0.0/10` paylaşımlı aralığındaydı. Bu nedenle yalnızca modemden port yönlendirmek dışarıdan IPv4 erişimi sağlamıyordu. Genel IP talep etmek yerine **Cloudflare Quick Tunnel** ile devam ettik.

Bu yöntem alan adı satın almadan ve modemden port açmadan geçici bir HTTPS adresi üretir. Trafik Cloudflare üzerinden geçer. Kalıcı şirket dağıtımı için kullanılabilirlik garantisi olan bir kurulumun yerine geçmez. [Quick Tunnel açıklaması](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/).

**Ubuntu / SSH:**

```bash
sudo docker run -d --name remotedesk-tunnel --network host cloudflare/cloudflared:latest tunnel --no-autoupdate --url http://127.0.0.1:8080
```

Adresi görmek için:

```bash
sudo docker logs --tail=100 -f remotedesk-tunnel
```

Logda buna benzer, size özel bir adres çıkar:

```text
https://rastgele-kelimeler.trycloudflare.com
```

Kendi logunuzdaki adresi kullanın. **Ctrl+C** log izlemeyi bitirir; arka plandaki container çalışmaya devam eder.

Adresin oluşması tek başına tünelin bağlandığını kanıtlamaz. `Registered tunnel connection` satırını ve aşağıdaki testi kontrol edin:

```bash
curl -i --max-time 20 https://rastgele-kelimeler.trycloudflare.com/ws
```

Örnek alan adını kendi adresinizle değiştirin. **400** beklenir. Zaman aşımı, 502 veya 530 varsa sunucu ve tünel loglarını inceleyin; bağlantı kurulmadan istemci testine geçmeyin.

ICMP proxy/ping izni uyarısı HTTP/WebSocket testi için tek başına engel değildir. Diğer hataları bağlantı sonucuyla birlikte değerlendirin.

## 11. Windows uygulamasını bağlayın

Sunucu ile uyumlu güncel Windows istemcisini kullanın. Kaynaktan oluşturmak gerekiyorsa **Windows'ta** .NET 10 SDK kurun; aynı `new-branch` kaynaklarının bulunduğu depo kökünde çalıştırın:

```powershell
dotnet publish "stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti.csproj" -c Release -r win-x64 --self-contained true -o artifacts/client-win-x64
```

Çıktı klasörünün tamamını kullanın; yalnızca EXE'yi kopyalamayın.

Uygulamadaki sunucu alanına, tünel adresinin başını **wss** yapıp sonuna **/ws** ekleyin:

```text
wss://rastgele-kelimeler.trycloudflare.com/ws
```

1. **Şifresiz yerel test** seçeneği kapalı kalsın.
2. İki istemcide de aynı sunucu adresini kullanın.
3. İlk denemede **JPEG** aktarımını seçin.
4. İki istemciyi sunucuya bağlayın.
5. Ekran sahibi kod oluştursun; diğer taraf kodla bağlantı istesin.
6. Ekran sahibi isteği kabul etsin.
7. Kontrol için ayrıca fare/klavye izni verin.
8. İzni geri almayı ve paylaşımı durdurmayı da deneyin.

İsterseniz EXE yanındaki `sunucu.json` dosyasına adresi kaydedin:

```json
{
  "SunucuAdresi": "wss://rastgele-kelimeler.trycloudflare.com/ws"
}
```

Anahtar doğrulaması varsayılan olarak kapalıdır. Bağımsız eş doğrulaması için iki istemcide etkinleştirip tam güvenlik kodlarını telefon gibi güvenilir ayrı bir kanaldan karşılaştırın.

## 12. Farklı internetlerden test edin

1. Ubuntu'nun çalıştığı ana bilgisayar ev internetinde kalsın.
2. İkinci Windows bilgisayarı ev Wi-Fi'sinden ayırıp telefonun mobil erişim noktasına bağlayın.
3. Her iki uygulamada aynı tünel adresiyle bağlantı kurun.
4. Görüntü, kontrol izni, izin geri alma ve bağlantıyı bitirmeyi sınayın.

WebRTC farklı ağlarda ayrıca TURN gerektirebilir; bu rehber TURN kurmaz. Dış erişimi ilk olarak JPEG ile doğrulayın.

**Bu oturumda doğrulananlar:** Ubuntu/Docker kurulumu, relay'in 400 yanıtı, yeniden başlatmadan sonra relay'in otomatik açılması ve aynı internet üzerindeki istemcilerle tünel adresinden bağlantı. Farklı internet testi henüz kullanıcı tarafından doğrulanmadı.

## 13. Bilgisayar yeniden başlatılırsa

1. VirtualBox'tan Ubuntu'yu açın. Windows'un açılması Ubuntu'nun otomatik başladığı anlamına gelmez.
2. Ubuntu'da `hostname -I` ile IP'yi kontrol edin.
3. Windows PowerShell'den SSH ile bağlanın.
4. Relay'i kontrol edin:

```bash
sudo docker ps
curl -i http://127.0.0.1:8080/ws
```

Relay başlamadıysa:

```bash
sudo systemctl start docker
sudo docker start remotedesk-server
```

Tüneli ilk kurulumda otomatik başlatma politikası olmadan oluşturduk. Durumunu kontrol edin; durmuşsa başlatın:

```bash
sudo docker ps -a --filter name=remotedesk-tunnel
sudo docker start remotedesk-tunnel
sudo docker logs --since=5m -f remotedesk-tunnel
```

**Yeni tünel adresi oluşabilir.** Son logdaki adresi iki istemcide de güncelleyin. Sunucuyu yeniden derlemeniz gerekmez. Ubuntu veya ana bilgisayar kapanır/uykuya girerse dış erişim kesilir.

## 14. Testi kapatın veya kodu güncelleyin

İnternet tünelini kapatmak için:

```bash
sudo docker stop remotedesk-tunnel
```

Relay'i de durdurmak için:

```bash
sudo docker stop remotedesk-server
```

Kod güncellemesi gerekiyorsa aktif oturumların kapanacağını dikkate alarak, yerel değişikliği olmayan checkout üzerinde:

```bash
cd ~/intern.rdp
git pull --ff-only
sudo docker build -f deploy/Dockerfile -t remotedesk-server .
```

**Yeni imaj başarıyla derlendikten sonra** eski container'ı değiştirin:

```bash
sudo docker stop remotedesk-server
sudo docker rm remotedesk-server
sudo docker run -d --name remotedesk-server --restart unless-stopped -p 8080:8080 remotedesk-server
curl -i http://127.0.0.1:8080/ws
```

Bu uygulamada oturum ve bağlantı kodları bellektedir; yeniden başlatmada kaybolur. Tünel çalışmaya devam ediyorsa adresini yeniden kullanabilirsiniz; bağlantıyı tekrar kontrol edin.

## Kısa sorun giderme

| Belirti | Kontrol |
| --- | --- |
| Parola yazılmıyor gibi | Linux parola girişinde karakter göstermez; yazıp Enter'a basın. |
| SSH bağlantısı kurulamıyor | Ubuntu açık mı, IP değişti mi, köprü doğru ağ kartında mı? |
| Docker komutu çıktı vermedi | Bazı kurulum komutları sessizdir; ilgili dosya/sürüm kontrolünü çalıştırın. |
| Container adı zaten kullanımda | `sudo docker ps -a` ile kontrol edin; yeniden `run` yerine mevcut container'ı `start` edin. |
| /ws adresi 400 döndürüyor | Normal HTTP kontrolünde beklenen yanıt; istemciden WebSocket bağlantısını ayrıca sınayın. |
| Tünel 502 veriyor | Relay çalışıyor mu, Ubuntu'da `http://127.0.0.1:8080/ws` yanıt veriyor mu? |
| Tünel adresi artık çalışmıyor | Güncel tünel logundaki adresi kontrol edin; yeniden başlatmada değişebilir. |
| Kod/istek hız sınırı | Bir dakika bekleyin. Proxy arkasındaki kullanıcılar sunucuda ortak IP bütçesini paylaşabilir. |
| Ubuntu diski doluyor | `df -h` ve `sudo docker system df` ile kullanımı inceleyin; yedekleri/volume'leri gelişigüzel silmeyin. |

## Şirkete aktarırken

Bu rehber **sanal makine + geçici tünel ile test** yoludur. Doğrudan alan adı, Caddy sertifikası ve 80/443 port yönlendirmesi bu oturumda uçtan uca denenmedi.

Kalıcı kurulumda şirketin BT ekibiyle genel IP/erişim yöntemi, kalıcı alan adı, TLS, servislerin açılışta başlaması ve işletim sorumluluğu belirlenmelidir. Geçici `trycloudflare.com` adresini sabit şirket sunucu adresi olarak dağıtmayın.

