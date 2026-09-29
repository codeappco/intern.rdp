using System.Net;
using System.Net.Http;

namespace stajprojesi_uzaktan_baglanti;

internal static class RenderSunucu
{
    public static bool RenderAdresi(Uri adres) =>
        adres.Scheme == "wss" && adres.Host.EndsWith(".onrender.com", StringComparison.OrdinalIgnoreCase);

    public static async Task UyandirAsync(Uri adres, CancellationToken iptal)
    {
        var saglikAdresi = new UriBuilder(adres)
        {
            Scheme = "https", Port = -1, Path = "/health", Query = "", Fragment = ""
        }.Uri;
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        while (true)
        {
            iptal.ThrowIfCancellationRequested();
            using var deneme = CancellationTokenSource.CreateLinkedTokenSource(iptal);
            deneme.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                using var cevap = await http.GetAsync(saglikAdresi, deneme.Token);
                if (cevap.IsSuccessStatusCode)
                {
                    string icerik = await cevap.Content.ReadAsStringAsync(deneme.Token);
                    // Render'in acilis sayfasi HTTP 200 donse de relay hazir olmayabilir.
                    try
                    {
                        using var belge = System.Text.Json.JsonDocument.Parse(icerik);
                        if (belge.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                            belge.RootElement.TryGetProperty("durum", out var durum) &&
                            durum.ValueKind == System.Text.Json.JsonValueKind.String && durum.GetString() == "hazir")
                            return;
                    }
                    catch (System.Text.Json.JsonException) { }
                }
                else if ((int)cevap.StatusCode < 500 && cevap.StatusCode != HttpStatusCode.TooManyRequests)
                    cevap.EnsureSuccessStatusCode();
            }
            catch (OperationCanceledException) when (!iptal.IsCancellationRequested) { }
            await Task.Delay(TimeSpan.FromSeconds(2), iptal);
        }
    }
}
