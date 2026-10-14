# RemoteDesk Windows ve CMD güvenlik incelemesi
Tarih: 10 Ekim 2026. Kapsam: mevcut WPF ve ASP.NET kaynakları; önceki yayımlanmış EXE'ler ve adresi bilinmeyen canlı sunucu bu incelemenin dışında.

## Kanıtlanan davranışlar ve düzeltmeler

### 1. Hatalı fare komutunun kısmen uygulanması — düşük etkili giriş doğrulama sorunu
Konum: [UzakGirdi.cs:56](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/UzakGirdi.cs:56>) ve [UzakGirdi.cs:70](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/UzakGirdi.cs:70>).
Önceki akışta koordinatlar geçerliyse fare hareketi, düğme numarası/basılı durumu veya tekerlek miktarı doğrulanmadan uygulanıyordu. Geçersiz düğme 99 veya aralık dışı tekerlek 1201 içeren mesaj böylece kısmi yan etki üretebiliyordu.
Düzeltme: tüm komut alanları hareketten önce doğrulanıyor. Regresyon, hatalı/missing alanlarda sahte Windows alıcısına hiç olay gönderilmediğini doğruluyor.
**Sınır:** bu yol zaten onaylı, E2EE doğrulanmış ve açık kontrol izni olan oturum gerektirir. Yetkisiz kontrol veya ayrıcalık yükseltme olarak sınıflandırılmadı.

### 2. Başarısız tuş bırakma kaydının kaybolması — koşullu işlevsel güvenlik riski
Konum: [UzakGirdi.cs:82](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/UzakGirdi.cs:82>).
Önceki Release, Windows bırakma işlemini reddetse bile tuş/düğme listesini tamamen siliyordu. Sonraki temizlikte yeniden bırakma denenemiyordu.
Düzeltme: yalnızca başarılı bırakılan kayıt kaldırılıyor; başarısızlar sonraki Release için tutuluyor.
Regresyon sahte alıcıyla ilk bırakmayı başarısız kılıyor, ikinci çağrının yeniden denediğini ve başarı sonrası tekrar olay üretilmediğini doğruluyor.
**Sınır:** Windows'un gerçek güvenli masaüstünde takılı kalan tuş olayı yeniden üretilmedi. İşletim sisteminin reddettiği SendInput çağrısının hemen başarılı olacağı garantisi yoktur. Kontrol izninin kaldırılması yine anında yeni uzak girdiyi engeller.

### 3. Geçici hassas verinin gereksiz kopyaları — bellek temizliği iyileştirmesi
Konumlar: [SecureSession.cs:40](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/SecureSession.cs:40>), [SecureSession.cs:86](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/SecureSession.cs:86>), [EkranYakala.cs:54](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/EkranYakala.cs:54>), [MainWindow.xaml.cs:111](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/MainWindow.xaml.cs:111>), [MainWindow.Security.cs:74](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/MainWindow.Security.cs:74>), [MainWindow.xaml.cs:78](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/MainWindow.xaml.cs:78>).
- Şifre çözme sonrasında oluşturulan ek immutable düz metin string kaldırıldı; Stream üzerinden JsonDocument oluşturuluyor, kaynak byte dizisi finally içinde temizleniyor.
- HKDF hata yolu da ortak sırrı temizleyen finally kapsamına alındı.
- JPEG üretim tamponları ve gönderilen/alınan geçici JPEG dizileri kullanım sonunda sıfırlanıyor.
- Oturum temizliğinde görüntü Source referansı açıkça kaldırılıyor.
Mevcut AES nesneleri ve ECDH oturum sonunda Dispose ediliyor; geçici anahtar materyali temizleniyor. E2EE/replay regresyonu yeni JSON ayrıştırma yolundan sonra da çalışıyor.
**Sınır:** bu değişiklikler uzaktan anahtar sızıntısını kanıtlamaz. Bellek dökümüyle sömürü gösterilmedi. Protokolün Base64 stringleri, WPF çözümlenmiş görüntüsü ve yönetilen kopyalar nedeniyle tüm hassas verinin anında RAM'den silindiği iddia edilmiyor.

### 4. Bozuk sayısal mesajlarda tutarsız kapanış — protokol dayanıklılığı
Konum: [Program.cs:395](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/RemoteDesk.Server/Program.cs:395>).
GetInt64/GetInt32 gibi okumalar sayı aralığı aşıldığında FormatException/OverflowException üretebiliyordu; bunlar önceki mesaj hata filtresinde yoktu. Bu durum ilgili isteğin işlenmeyen hatayla sonlanmasına yol açabilirdi; diğer oturumların ele geçirilmesi gösterilmedi.
Düzeltme: bozuk JSON, alan türü ve sayısal aralık hatalarında ilgili bağlantının temizliği yapılıyor ve 1007 kapanış kodu gönderiliyor. Ham mesajlar loglanmıyor.
Testler bozuk JSON, dizi kök, sayısal tip alanı ve 1e100 zaman değerini gönderirken bağımsız onaylı oturumun iletiminin devam ettiğini doğruluyor. Hatalı onay şekli mevcut bilgi cevabını koruyor.

## İstemciId bulgusu

İyi biçimli ek istemciId alanının bağlantıyı kapattığı yeniden üretilemedi. Sunucu kimliği soket üzerinde atar; istemci tarafından gönderilen alan yetki kaynağı değildir.
Yeni testler null, boolean, sayı, string ve iç içe nesne değerlerini kabul edilen mesajlara ekler. Başkasının kimliğiyle onay, mesaj enjeksiyonu ve oturum bitirme girişimi sonuçsuz kalır; gerçek katılımcının şifreli zarfı yönlendirilir.
Geçersiz JSON/alan türü ile yalnızca fazladan bir alan eklemek aynı şey değildir. Kullanıcının CMD testinin tam girdisi ve canlı sunucu sürümü bilinmediğinden dış testin kesin kök nedeni belirlenmiş sayılmaz.
Hatalı mesaj gönderen aktif oturum üyesinin kendi oturumu güvenli biçimde sonlandırılabilir; bunun partnerine oturum_bitti gitmesi beklenir. “İzolasyon” bağımsız diğer oturumların etkilenmemesi anlamındadır.

## İstenen yedi Windows kontrolü

| Kontrol | Kanıt / sonuç |
|---|---|
| Onaysız ekran paylaşımı | [MainWindow.Security.cs:16](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/MainWindow.Security.cs:16>) sahibin onayladığı istek kimliğini kontrol eder; [MainWindow.Security.cs:80](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/MainWindow.Security.cs:80>) iki taraf doğrulanmadan aktivasyon yapmaz; [MainWindow.xaml.cs:144](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/MainWindow.xaml.cs:144>) ekran başlatmayı tekrar denetler. WPF testleri onay öncesinde paylaşım görevi olmadığını doğrular. |
| Açık kontrol izni ve geri çekme | [MainWindow.Kontrol.cs:26](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/MainWindow.Kontrol.cs:26>) yalnızca doğrulanmış paylaşan için izin üretir; [MainWindow.Kontrol.cs:117](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/MainWindow.Kontrol.cs:117>) oturum/rol ve mevcut izin token'ını denetler. [MainWindow.Kontrol.cs:17](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/MainWindow.Kontrol.cs:17>) izni önce yerel olarak siler, sonra basılı girdileri bırakır. Test: izin yokken, geri çekildikten sonra ve eski token ile yeniden izin verildiğinde girdiler engellenir. |
| Oturum bitince durma | [MainWindow.xaml.cs:64](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/MainWindow.xaml.cs:64>) kripto/doğrulama/izin durumunu temizler, yakalama iptalini tetikler ve ekranı kaldırır. Yakalama tamamlanınca [MainWindow.xaml.cs:96](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/MainWindow.xaml.cs:96>) iptali ve oturum kimliğini yeniden kontrol eder. WPF testi iptal token'ını ve görüntü referansının kaldırılmasını doğrular. Halihazırda işletim sistemine teslim edilmiş girdiyi geriye dönük geri alma iddiası yoktur. |
| Anahtar ve log güvenliği | AES/ECDH anahtarı veya çözülmüş ekran/girdi yazan log çağrısı bulunmadı. Sunucunun deneme logları kimlik/pseudonym içerir; log regresyonu gönderilen kod ve içeriklerin sızmadığını kontrol eder. Bellek sınırlamaları yukarıda açıklanmıştır. |
| TLS doğrulaması | [ResponsiveWebSocket.cs:10](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/ResponsiveWebSocket.cs:10>) standart ClientWebSocket kullanır; üretim kaynaklarında sertifika doğrulamasını true döndürerek atlayan callback bulunmadı. Gerçek istemciyle yerel self-signed sertifika testi AuthenticationException nedeniyle reddedildi. [MainWindow.xaml.cs:235](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/MainWindow.xaml.cs:235>) ws için yalnızca açık yerel test tercihi ve yerel/özel adres kabul eder; bu TLS atlaması değil, bilerek seçilen şifresiz taşıma modudur. |
| Dosya ve işlem yetkileri | [MainWindow.Settings.cs:24](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/MainWindow.Settings.cs:24>) kullanıcı LocalApplicationData dizinindeki sabit ayar yolunu kullanır; [MainWindow.Settings.cs:45](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/MainWindow.Settings.cs:45>) rastgele geçici dosyaya yazıp değiştirir. Uzak mesajdan dosya yolu veya çalıştırılacak komut alınmıyor. Uygulama kaynaklarında Process.Start/shell çalıştırma noktası bulunmadı. İşletim sistemi ACL'leri ve uygulamanın çalıştırıldığı kullanıcı yetkisi geçerlidir; yükseltilmiş Windows token/kurulum ACL testi yapılmadı. |
| Güncelleme | İncelenen çalışma zamanı kaynaklarında indirme/otomatik güncelleme mekanizması bulunmadı. Olmayan bir mekanizma için “imza doğrulama açığı” raporlanmadı; harici kurulum/yayımlama altyapısı kapsam dışı. |

Ayar adında kontrol/format karakterleri temizlenir; FPS ve çözünürlük sabit kabul listesine dönüştürülür: [MainWindow.Settings.cs:12](<C:/Users/krali/Desktop/agalaryazilim/STAJ/UZAKTAN BAGLANTI/intern.rdp/stajprojesi uzaktan baglanti/stajprojesi uzaktan baglanti/MainWindow.Settings.cs:12>). Bu dosyada uzaktan path traversal veya işlem başlatma yolu bulunmadı.

## Önceki relay kontrolleri ve testler

Yeni kod eski kodu iptal eder; kabul edilen kod tüketilir; sahip kopunca kod ve bekleyen istekler temizlenir. Normal ve sessiz TCP kaybı senaryoları mevcut V3/V4/V5 testlerinde yer alır.
Origin politikası varsayılan masaüstü erişimini korur, açık web izin listesi dışında Origin'i reddeder ve kimlik doğrulama yerine kullanılmaz.
Üretim ve bağlantı denemesi için ayrı 10/istemci/dakika ve 30/IP/dakika bütçeleri devam eder. IP adresi taşıma katmanından gelir; sahte forwarding başlığıyla değişmez. Limitler süreç içidir; çok sunuculu/ters vekilli gerçek dağıtım ayrıca yapılandırılmalıdır.
AES-256-GCM, P-256 ECDH, HKDF, replay sırası, doğrulama kodu karşılaştırması, oturum katılımcısı yetkisi ve düz metin geri dönüşünün reddi korunur.

Çalıştırma: tests/RelaySecurity.Tests.ps1. Ek testler: WpfSecurity.Tests/InputTests.cs, RelaySecurity.Tests/ProtocolTests.cs ve TlsTests.cs.
Giriş testleri gerçek Windows'a tuş/fare basmaz; gerçek üretim sınıfına sahte alıcı bağlar. Gerçek WPF onay/temizlik kodu ayrıca test edilir. TLS testi sertifikayı güvenilen depoya eklemez; yalnızca geçici sunucu sertifikası kullanır.
NuGet “bilinen açık yok” ifadesi kullanıcının önceki tarama sonucudur; bu incelemede çevrimiçi NuGet zafiyet taraması yeniden yapılmadı.

Son doğrulama: Windows/WPF, TLS, protokol izolasyonu, üretim/deneme hız limitleri, V3/V4/V5 kopma ve kod yaşam döngüsü, MITM/E2EE/replay testlerinin tamamı geçti. Sunucu, Windows istemcisi ve test projeleri 0 hata / 0 uyarıyla derlendi. Dağıtım yapılmadı. Protokol/TLS çıktısı .test-build/security-server/protocol-results.log dosyasında saklanıyor.
