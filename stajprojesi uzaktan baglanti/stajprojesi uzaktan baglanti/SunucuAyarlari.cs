using System.IO;
using System.Text.Json;

namespace stajprojesi_uzaktan_baglanti;

internal static class SunucuAyarlari
{
    private static string KayitYolu => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RemoteDesk", "sunucu.json");

    public static string Yukle()
    {
        // Dagitimdaki varsayilan, kullanicinin son basarili adresiyle degistirilebilir.
        foreach (string yol in new[] { KayitYolu, Path.Combine(AppContext.BaseDirectory, "sunucu.json") })
        {
            try
            {
                using var belge = JsonDocument.Parse(File.ReadAllText(yol));
                if (belge.RootElement.TryGetProperty("SunucuAdresi", out var alan) &&
                    alan.ValueKind == JsonValueKind.String &&
                    Uri.TryCreate(alan.GetString(), UriKind.Absolute, out var adres) &&
                    adres.Scheme == "wss" && adres.UserInfo.Length == 0 && adres.Fragment.Length == 0)
                    return adres.AbsoluteUri;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return "wss://localhost:7007/ws";
    }

    public static bool Kaydet(Uri adres)
    {
        // Sifresiz yerel test her acilista yeniden ve acikca secilir.
        if (adres.Scheme != "wss") return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KayitYolu)!);
            File.WriteAllText(KayitYolu, JsonSerializer.Serialize(new { SunucuAdresi = adres.AbsoluteUri }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
