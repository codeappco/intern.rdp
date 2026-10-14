# RemoteDesk — Adım adım kurulum

Bu rehberle uygulamayı kendi sunucunuzda çalıştırıp iki Windows bilgisayarı birbirine bağlayabilirsiniz.

**1–5. adımlar Linux sunucuda, 6–8. adımlar Windows bilgisayarda yapılır.** Örneklerdeki `remote.firma.com` adresini kendi alan adınızla değiştirin.

## 1. Sunucuyu hazırlayın

Gerekenler:

- İnternete açık bir Linux sunucu.
- Sunucuda Git, Docker Engine ve Docker Compose.
- Size ait bir alan adı veya alt alan adı.

Docker kurulu değilse [resmî kurulum rehberini](https://docs.docker.com/engine/install/) uygulayın. Ubuntu kullanıyorsanız [Ubuntu adımlarını](https://docs.docker.com/engine/install/ubuntu/) izleyin.

Sunucunun terminalinde kontrol edin:

```bash
git --version
sudo docker version
sudo docker compose version
```

Üç komut da hata vermeden çalışıyorsa devam edin.

## 2. Alan adını sunucuya yönlendirin

Alan adınızın DNS paneline girip şu kaydı oluşturun:

| Tür | Ad | Değer |
| --- | --- | --- |
| A | remote | Sunucunuzun genel IPv4 adresi |

Örneğin alan adınız `firma.com` ise bağlantı adresiniz `remote.firma.com` olur.

Sunucu sağlayıcınızın ve sunucunuzun güvenlik duvarında **TCP 80 ve 443** portlarını açın. Bu portları başka bir uygulama kullanmamalı. IPv6 erişiminiz yoksa bu alt alan adına AAAA kaydı eklemeyin.

Portların kullanım amacı:

| Port | Ne için kullanılıyor? | İnternete açılacak mı? |
| --- | --- | --- |
| TCP 80 | HTTPS sertifikası ve HTTP yönlendirmesi | Evet |
| TCP 443 | Windows uygulamalarının güvenli bağlantısı | Evet |
| TCP 8080 | Caddy ile relay arasındaki Docker içi bağlantı | Hayır |

Sunucu bir modem/router arkasındaysa 80 ve 443 portlarını sunucunun yerel IP adresine yönlendirin. CGNAT arkasında yalnızca port yönlendirmek yeterli değildir; dışarıdan erişilebilir bir genel IP gerekir. Windows istemcilerinde gelen bağlantı portu açmanız gerekmez; iki istemci de sunucuya bağlanır.

DNS kaydını kontrol etmek için:

```bash
nslookup remote.firma.com
```

Doğrudan bu sunucuya yönlendirdiğiniz kayıtta sunucunun genel IP adresini görmelisiniz. Yeni DNS kaydı henüz görünmüyorsa yayılmasını bekleyin.

## 3. Projeyi indirin

Sunucunun terminalinde:

```bash
git clone https://github.com/codeappco/intern.rdp.git
cd intern.rdp
```

Sonraki sunucu komutlarını bu klasörün içinde çalıştırın.

## 4. Alan adınızı ayar dosyasına yazın

```bash
cp deploy/.env.example deploy/.env
nano deploy/.env
```

İçindeki satırı kendi alan adınızla değiştirin:

```dotenv
REMOTEDESK_DOMAIN=remote.firma.com
```

Buraya `https://`, `wss://` veya `/ws` eklemeyin.

Nano'da kaydetmek için **Ctrl+O**, **Enter**, çıkmak için **Ctrl+X** tuşlarına basın.

### Hangi dosya neyi ayarlıyor?

| Dosya | Görevi |
| --- | --- |
| `deploy/.env` | Kendi alan adınızı burada belirtirsiniz. |
| `deploy/compose.yaml` | Servisler, portlar, sunucu ayarları ve yeniden başlatma davranışı. |
| `deploy/Caddyfile` | HTTPS bağlantısını uygulama sunucusuna yönlendirir. Normal kurulumda değiştirmeniz gerekmez. |
| Windows EXE'sinin yanındaki `sunucu.json` | Windows uygulamasının hangi sunucuya bağlanacağını belirler. |

Normal kurulumda sunucu tarafında yalnızca `.env` dosyasını değiştirmeniz yeterlidir. Caddy dışarıdan gelen `wss://remote.firma.com/ws` bağlantısını Docker ağı içindeki `relay:8080` adresine iletir. Sunucuda ayrıca .NET kurmanız gerekmez; Docker imajı gerekli çalışma zamanını içerir.

### Bağlantı kodunun geçerlilik süresini değiştirme

`deploy/compose.yaml` dosyasında `relay` → `environment` altındaki şu satırı bulun:

```yaml
      ConnectionCodeLifetimeSeconds: "300"
```

`300` beş dakika demektir. Örneğin iki dakika için `"120"` yazın. Desteklenen aralık **1–300 saniyedir**. `ASPNETCORE_URLS: http://+:8080` ve `ASPNETCORE_ENVIRONMENT: Production` satırlarını bu kurulumda olduğu gibi bırakın.

`.env` veya Compose ayarlarını değiştirdikten sonra kontrol edip uygulayın:

```bash
sudo docker compose --env-file deploy/.env -f deploy/compose.yaml config --quiet
sudo docker compose --env-file deploy/.env -f deploy/compose.yaml up -d
```

İlk komut hata vermeden tamamlanmalı. Ayar değişikliği servisi yeniden oluşturursa aktif bağlantılar kesilir. Alan adını değiştirirseniz DNS kaydını ve iki Windows uygulamasındaki `sunucu.json` adresini de güncelleyin.

## 5. Sunucuyu çalıştırın

```bash
sudo docker compose --env-file deploy/.env -f deploy/compose.yaml up -d --build
```

İlk çalıştırmada dosyalar indirilir ve sunucu derlenir. HTTPS sertifikası otomatik hazırlanır.

Sertifika alınabilmesi için alan adının bu sunucuya yönlenmesi ve 80/443 erişiminin çalışması gerekir. Caddy sertifika verilerini `caddy_data` adlı Docker volume'ünde saklar. Bu veriyi koruduğunuzda container yeniden oluşturulsa da sertifika durumu korunur.

Durumunu kontrol edin:

```bash
sudo docker compose --env-file deploy/.env -f deploy/compose.yaml ps
```

`relay` ve `caddy` servisleri **Up** görünmeli. Kendi adresinizle bağlantıyı kontrol edin:

```bash
curl -i https://remote.firma.com/ws
```

**400 yanıtı bu kontrolde normaldir.** Bu adres web sayfası değil, uygulamanın bağlantı noktasıdır. Sertifika hatası veya 502 alırsanız logları açın:

```bash
sudo docker compose --env-file deploy/.env -f deploy/compose.yaml logs --tail=100 relay caddy
```

Logları canlı izlemek için:

```bash
sudo docker compose --env-file deploy/.env -f deploy/compose.yaml logs --tail=50 -f relay caddy
```

**Ctrl+C** yalnızca log izlemeyi kapatır; sunucu çalışmaya devam eder. `relay` uygulamanın, `caddy` ise HTTPS ve yönlendirme katmanının logudur.

### Sunucu kapanıp açılırsa

Compose dosyasındaki `restart: unless-stopped`, elle durdurmadığınız servisleri Docker yeniden başladığında çalıştırır. Bunun için Docker hizmetinin de sunucu açılışında başlaması gerekir. Ubuntu/systemd üzerinde kontrol edebilirsiniz:

```bash
sudo systemctl is-enabled docker
sudo systemctl status docker --no-pager
```

Otomatik başlatma kapalıysa etkinleştirin:

```bash
sudo systemctl enable --now docker
```

Uygulama veritabanı kullanmaz. Aktif bağlantılar ve kodlar bellekte tutulduğu için relay yeniden başladığında kullanıcılar tekrar bağlanıp yeni kod oluşturmalıdır. Bu kurulumda **tek relay** çalıştırın; iki kullanıcının farklı relay süreçlerine bağlanması eşleşmeyi bozar.

Windows uygulamasına yazacağınız adres artık hazır:

```text
wss://remote.firma.com/ws
```

## 6. Windows uygulamasını oluşturun

Bu adımdan itibaren **Windows bilgisayarınızda** çalışın.

1. [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) kurun.
2. Projeyi GitHub'dan indirin veya Git ile klonlayın.
3. `README.md` dosyasının bulunduğu klasörde PowerShell açın.
4. Aşağıdaki komutu çalıştırın:

```powershell
dotnet publish "stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti.csproj" -c Release -r win-x64 --self-contained true -o artifacts/client-win-x64
```

Uygulama `artifacts/client-win-x64` klasöründe oluşur. Bu paket 64 bit Windows içindir; kullanıcıların ayrıca .NET kurması gerekmez.

## 7. Uygulamaya sunucu adresini ekleyin

Aynı PowerShell penceresinde:

```powershell
Copy-Item deploy/sunucu.example.json artifacts/client-win-x64/sunucu.json
notepad artifacts/client-win-x64/sunucu.json
```

Dosyadaki adresi kendi adresinizle değiştirip kaydedin:

```json
{
  "SunucuAdresi": "wss://remote.firma.com/ws"
}
```

**`artifacts/client-win-x64` klasörünün tamamını iki Windows bilgisayara da kopyalayın.** Yalnızca EXE dosyasını göndermeyin. `sunucu.json`, EXE'nin yanında kalmalı.

## 8. İki bilgisayarı bağlayın

1. İki bilgisayarda da `stajprojesi uzaktan baglanti.exe` dosyasını açın.
2. Sunucu adresini kontrol edip **Sunucuya bağlan** düğmesine basın.
3. Ekranını paylaşacak kişi bağlantı kodu oluştursun.
4. Diğer kişi bu kodu kendi uygulamasına girip bağlantı isteği göndersin.
5. Ekran sahibi gelen isteği kabul etsin.
6. Fare ve klavye kontrolü gerekiyorsa ekran sahibi ayrıca kontrol izni versin.
7. İşiniz bitince paylaşımı durdurun.

İlk denemeyi varsayılan **JPEG** aktarımıyla yapın; ek STUN/TURN kurulumu gerekmez. Bağlantı kodu en fazla 5 dakika geçerlidir ve kabul edilince tekrar kullanılamaz.

Anahtar doğrulaması varsayılan olarak kapalıdır. Kullanmak için iki uygulamada ayarlardan açın ve güvenlik kodlarını telefon gibi bağımsız bir kanaldan karşılaştırın.

## Daha sonra: güncelleme ve durdurma

Sunucuda proje klasörünün içindeyken güncellemek için:

```bash
git pull --ff-only
sudo docker compose --env-file deploy/.env -f deploy/compose.yaml up -d --build
```

Güncelleme aktif bağlantıları kesebilir. İstemci kodu değiştiyse 6–7. adımlarla yeni Windows paketini de hazırlayın.

Sunucuyu durdurmak için:

```bash
sudo docker compose --env-file deploy/.env -f deploy/compose.yaml down
```

Sertifika verilerini korumak için bu komuta `-v` eklemeyin.

## Takılırsanız

| Sorun | Yapılacak işlem |
| --- | --- |
| Sunucuya bağlanmıyor | Adresin `wss://alan-adiniz/ws` biçiminde olduğunu ve servislerin çalıştığını kontrol edin. |
| Sertifika hatası | DNS kaydını, TCP 80/443 erişimini ve Caddy loglarını kontrol edin. |
| Sunucu adresi boş | `sunucu.json` dosyasını EXE'nin yanına koyun veya adresi elle girin. |
| Kod geçersiz | Yeni kod oluşturun; iki bilgisayar aynı sunucuya bağlı olmalı. |
| Çok fazla deneme | Bir dakika bekleyin. Bu kurulumda kullanıcılar ortak 30/dakika IP sınırını paylaşır. |
| Kontrol çalışmıyor | Ekran sahibinin ayrıca fare/klavye izni verdiğini kontrol edin. |
| WebRTC kullanmak istiyorsanız | İki bilgisayara WebView2 Runtime kurun, iki uygulamada da WebRTC seçin. Bazı ağlarda ayrıca TURN sunucusu gerekir; bu kurulum TURN içermez. |
