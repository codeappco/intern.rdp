# Farklı ağlardan RemoteDesk bağlantısı

**Render ücretsiz kurulumu için [RENDER.md](RENDER.md) rehberini kullanın.**
Aşağıdaki Docker Compose ve Caddy adımları kendi VPS sunucunuz içindir.

İki Windows istemcisi aynı merkezi sunucuya `wss://ALAN_ADI/ws` üzerinden bağlanır.
Kod, onay, ekran görüntüsü ve kontrol mesajları sunucudan aktarılır. İstemci
modemlerinde port yönlendirme gerekmez; CGNAT arkasındaki istemciler de dışarıya
443 bağlantısı kurabildikleri sürece bu yapıyı kullanabilir.

```text
Windows A ── WSS / 443 ── Caddy ── relay:8080 ── Windows B (Caddy üzerinden WSS)
```

## Gerekenler

- İnternetten erişilebilen Linux sunucu ve Docker Engine + Compose eklentisi.
- Sunucunun genel IP adresine yönlendirilmiş bir alan adı (ör. `remote.example.com`).
- Sunucu güvenlik duvarında ve sağlayıcı panelinde gelen TCP 80 ve 443 açık olmalı.
  8080'i internete açmayın; Compose yalnızca Caddy'nin portlarını yayınlar.
- DNS A kaydı sunucuya yönelmeli. AAAA kaydı varsa IPv6 da doğru sunucuya ulaşmalı.
  İlk kurulumda DNS kaydını doğrudan sunucuya yönlendirin.

## Sunucuyu kurma

Depoyu sunucuya kopyalayın. `intern.rdp/deploy` klasöründe:

```sh
cp .env.example .env
```

`.env` içindeki iki örnek değeri kendi bilgilerinizle değiştirin. Alan adına
`https://`, port veya `/ws` eklemeyin:

```dotenv
REMOTEDESK_DOMAIN=remote.example.com
ACME_EMAIL=admin@example.com
```

Ardından aynı klasörde:

```sh
docker compose up -d --build
docker compose ps
docker compose logs --tail=100 caddy relay
curl --fail https://remote.example.com/health
```

Sağlık yanıtı `{"durum":"hazir"}` olmalı. Caddy alan adının TLS sertifikasını alır
ve yeniler. Sertifikalar `caddy_data` volume'ünde kalır; normal güncellemelerde
bu volume'ü silmeyin. Caddy WebSocket bağlantılarını da aktarır.

## Windows istemcisini hazırlama

Geliştirme bilgisayarında .NET 10 SDK ile, depo kökünde PowerShell'den:

```powershell
.\deploy\Publish-Client.ps1 -ServerUrl 'wss://remote.example.com/ws'
```

`artifacts/client-win-x64` klasörünün tamamını iki bilgisayara da kopyalayın.
Bu paket .NET çalışma zamanını içerir. EXE'nin yanındaki `sunucu.json` varsayılan
adresi belirler. Uygulama son başarılı WSS adresini
`%LOCALAPPDATA%\RemoteDesk\sunucu.json` içinde hatırlar; bu kayıt paket
varsayılanından önce okunur. Adresi değiştirmek için uygulamadaki sunucu
adresini düzenleyip yeniden bağlanın. Şifresiz yerel test tercihi kaydedilmez.

Hazır uygulamayı kullanıyorsanız iki bilgisayarda da adresi elle
`wss://remote.example.com/ws` yapmanız yeterlidir.

## Farklı ağlarda kabul testi

1. A bilgisayarını ev/iş ağına, B bilgisayarını telefon interneti gibi farklı bir ağa bağlayın.
2. İkisinde de aynı WSS sunucusuna bağlanın; yerel test kutusunu boş bırakın.
3. A'da **Bağlantı Al**, B'de A'nın koduyla **Bağlan** seçin.
4. A isteği kabul edince B ekranı görmeli. Başlangıçta kontrol izni kapalıdır.
5. A fare/klavye izni versin; B görüntüye tıklayıp kontrolü denesin.
6. A izni kaldırınca giriş durmalı. Paylaşımı durdurmak iki tarafta oturumu kapatmalı.
7. B'nin internetini kesin; bağlantı zaman aşımı sonrası A'da oturum kapanmalı.
   Yeniden bağlanmak ve yeni paylaşım için tekrar kullanıcı işlemi gerekir.

## İşletim ve sınırlar

- Tek relay örneği çalıştırın. Oturumlar bellektedir; yeniden başlatma oturumları
  kapatır. Birden çok kopyaya yük dağıtımı bu sürümde desteklenmez.
- Görüntü trafiği sunucu üzerinden geçtiği için sunucunun bant genişliğini kullanır.
  Bu sürüm JPEG aktarır; yüksek FPS video kodlama veya doğrudan P2P bağlantı içermez.
- WSS aktarımı istemci ile sunucu arasında şifrelidir. TLS Caddy'de sonlanır;
  relay mesajları okuyabilir. Bu yapı uçtan uca şifreleme değildir.
- Kodlar 5 dakika geçerlidir; yeni kod almak önceki kodu iptal eder. Bağlantısı
  kesilen istemcinin kodu silinir. Kod üretme ve bağlantı denemeleri bağlantı başına
  sınırlandırılır; bu sınır kapsamlı IP tabanlı kötüye kullanım koruması değildir.
- Üretimde deneme amaçlı HTTP kod üretme/sorgulama uçları kapalıdır.
- Güncelleme: `docker compose up -d --build`. Kapatma: `docker compose down`.
- Sertifika hatasında DNS, 80/443 erişimi ve Caddy loglarını kontrol edin.
  İstemcide sertifika doğrulamasını kapatmayın.

## Yerel doğrulama

Depo kökünde `pwsh -File tests/ScreenRelay.Tests.ps1` sunucuyu güncel kaynaktan
derler, 5099 portunda üretim modunda başlatır ve oturum/aktarım/izin testlerini
çalıştırır. Bu test gerçek DNS, TLS ve iki ayrı internet bağlantısının yerine geçmez.

Kaynaklar: [Caddy automatic HTTPS](https://caddyserver.com/docs/automatic-https),
[Caddy WebSocket proxy](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy),
[ASP.NET Core WebSockets](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets?view=aspnetcore-10.0).
