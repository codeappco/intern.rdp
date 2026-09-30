# GitHub olmadan Render kurulumu

Hedef imaj: `docker.io/bahadirdocker/remotedesk-relay:latest`.

## Docker Hub

Docker Hub'da `bahadirdocker` hesabıyla giriş yapın. `remotedesk-relay`
isimli bir repository oluşturun ve görünürlüğünü **Private** seçin.
İmajı göndermeden önce deponun Private olduğunu doğrulayın; otomatik depo
oluşturmak için doğrudan push kullanmayın.

Depo kökünde PowerShell komutları:

```powershell
docker build --platform linux/amd64 -f deploy/Dockerfile -t bahadirdocker/remotedesk-relay:latest .
docker login
docker push bahadirdocker/remotedesk-relay:latest
```

`docker login` kimlik doğrulamasını kendi tarayıcınızda tamamlayın. Parola veya
erişim anahtarını sohbet mesajına yazmayın.

## Render

1. Render panelinde **New → Web Service → Existing Image** seçin.
2. Image URL: `docker.io/bahadirdocker/remotedesk-relay:latest`.
3. Private image için registry credential ekleyin: Docker Hub, kullanıcı
   `bahadirdocker`, özel imajları okuyabilen bir Docker Hub erişim anahtarı.
   Anahtarı doğrudan Render'ın ilgili alanına girin.
4. Plan **Free**, bölge **Frankfurt**, health check **/health** seçin.
5. Ortam değişkenlerini ekleyin:

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:8080
PORT=8080
```

Başlatma komutunu boş bırakın; imajın kendi başlangıç komutu kullanılacak.
Bu yöntem `render.yaml` ve GitHub bağlantısı kullanmaz.

Servis Live olduğunda panelin verdiği gerçek adreste `/health` yolunu açın.
`{"durum":"hazir"}` yanıtından sonra Windows istemcilerindeki adresi
`wss://SIZE-VERILEN-ADRES.onrender.com/ws` yapın.

İmaj güncellendiğinde yeniden build/push yapıp Render'da manuel deploy başlatın.
Ücretsiz planın bekleme ve trafik sınırları için [Render rehberine](RENDER.md) bakın.

Resmî kaynak: https://render.com/docs/deploying-an-image
