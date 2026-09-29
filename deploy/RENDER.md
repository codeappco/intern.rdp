# Render ücretsiz kurulum

Render, relay sunucusunu Docker ile çalıştırır; HTTPS sertifikasını ve
`onrender.com` adresini sağlar. Bu kurulumda DuckDNS, VPS, Caddy veya Compose gerekmez.

## 1. Güncel dosyaları GitHub'a gönderin

Bu çalışma kopyasının deposu `https://github.com/codeappco/intern.rdp`, dalı `main`.
Render yerel dosyaları göremez; son değişiklikler önce bu depoya commit/push edilmeli.
`render.yaml`, `.dockerignore`, `deploy/Dockerfile`, güncel sunucu kaynakları ve
istemci değişikliklerini gönderin. `.env`, derleme çıktıları ve `.vs` dosyalarını
bu değişikliğe eklemeyin. `.gitignore`, daha önce izlemeye alınmış çıktıları
kendiliğinden Git takibinden çıkarmaz.

## 2. Render'da servisi oluşturun

1. https://dashboard.render.com adresinde GitHub hesabınızla giriş yapın.
2. **New → Blueprint** seçin ve `codeappco/intern.rdp` deposunu bağlayın.
   Depo listelenmiyorsa GitHub bağlantısına bu depoya erişim verin.
3. Dalı `main`, Blueprint dosyasını `render.yaml` seçin.
4. Oluşturulacak servisin planının **Free** olduğunu doğrulayın, Blueprint'i dağıtın.
5. Derleme/deploy loglarını izleyin; servis **Live** olduğunda panelde verilen
   `https://....onrender.com` adresini kopyalayın. Adresi servis isminden tahmin etmeyin.

YAML tek bir ücretsiz servis oluşturur. Veritabanı gerekmez. Sertifikayı Render yönetir.

### Web Service ekranını kullanıyorsanız

Blueprint yerine **New → Web Service** ile de aynı depoyu seçebilirsiniz:

| Alan | Değer |
|---|---|
| Name | `remotedesk-relay` (veya uygun başka isim) |
| Branch | `main` |
| Language / Runtime | `Docker` |
| Region | `Frankfurt` |
| Root Directory | Boş bırakın |
| Dockerfile Path | `deploy/Dockerfile` |
| Docker Build Context | `.` (alan varsa) |
| Instance Type | `Free` |
| Health Check Path | `/health` |

Environment Variables:

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:8080
PORT=8080
```

Build/Start komutu yazmayın; Dockerfile bunları belirliyor.
`Root Directory` olarak `deploy` seçmeyin: sunucu kaynakları bu klasörün dışında.

## 3. Sunucuyu doğrulayın

Panelin verdiği adresin sonuna `/health` ekleyip tarayıcıdan açın:

```text
https://SIZE-VERILEN-ADRES.onrender.com/health
```

Yanıt `{"durum":"hazir"}` olmalı. İlk açılış yaklaşık bir dakika sürebilir.
503 veya açılış sayfası geçici olabilir; sürekli hata varsa deploy loglarını kontrol edin.

## 4. İki Windows istemcisini bağlayın

Her ikisinde de sunucu adresi:

```text
wss://SIZE-VERILEN-ADRES.onrender.com/ws
```

Yerel test seçeneğini boş bırakın. Birinde kod üretin, diğerinde bu kodla bağlanın
ve paylaşan bilgisayarda isteği kabul edin. Gerçek farklı ağ testi için bir
bilgisayarı telefon internetine bağlayabilirsiniz.

Güncel istemci, `onrender.com` adreslerinde `/health` yanıtını en fazla 120 saniye
bekler ve ardından WebSocket açar. Bu yalnızca bağlantı düğmesine basıldığında
çalışır; sürekli uyanık tutma görevi oluşturmaz. Diğer sunucularda 10 saniyelik
bağlantı sınırı korunur. Özel alan adı kullanırsanız uyuyan sunucuyu önce tarayıcıda
`/health` üzerinden uyandırın.

Hazır Windows paketini gerçek adresle üretmek için depo kökünde:

```powershell
.\deploy\Publish-Client.ps1 -ServerUrl 'wss://SIZE-VERILEN-ADRES.onrender.com/ws'
```

`artifacts/client-win-x64` klasörünün tamamını iki bilgisayara kopyalayın.

## Ücretsiz plan sınırları

- 15 dakika gelen trafik olmazsa servis uyuyabilir. Yeniden başlatıldığında
  bellekteki kodlar ve oturumlar kaybolur; yeni kod ve yeniden onay gerekir.
- Aylık çalışma saati, derleme ve dışarıya veri aktarım kotaları vardır. Ekran
  aktarımı bant genişliği tüketir; kullanımı panelden izleyin.
- Ödeme yöntemi eklenmiş hesapta ek bant genişliği ücretlenebilir. Ödeme yöntemi
  olmayan hesapta kota aşımı ücretsiz servisleri ay sonuna kadar durdurabilir.
- WSS, istemci ile Render arasını şifreler. Relay mesajları okuyabilir;
  uçtan uca şifreleme değildir.

Kaynaklar: [Docker](https://render.com/docs/docker),
[Blueprint](https://render.com/docs/blueprint-spec),
[WebSocket](https://render.com/docs/websocket),
[ücretsiz plan](https://render.com/docs/free).
