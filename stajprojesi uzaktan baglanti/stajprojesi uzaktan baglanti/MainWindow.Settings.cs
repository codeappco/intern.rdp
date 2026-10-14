using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace stajprojesi_uzaktan_baglanti;

public sealed class UserSettings
{
    public string DisplayName { get; set; } = "";
    public string VideoTransport { get; set; } = "jpeg";
    public string StunUrl { get; set; } = "stun:stun.l.google.com:19302";
    public string TurnUrl { get; set; } = "";
    public string TurnUsername { get; set; } = "";
    public bool AdaptiveImage { get; set; } = true;
    public bool VerifyPeerKey { get; set; }
    public int Fps { get; set; } = 15;
    public int Height { get; set; } = 720;
    public void Normalize()
    {
        DisplayName = new string((DisplayName ?? "").Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).Take(32).ToArray()).Trim();
        if (DisplayName.Length == 0) DisplayName = "RemoteDesk-" + Guid.NewGuid().ToString("N")[..8];
        if (VideoTransport != "webrtc") VideoTransport = "jpeg";
        StunUrl = (StunUrl ?? "").Trim(); TurnUrl = (TurnUrl ?? "").Trim();
        TurnUsername ??= "";
        if (Fps is not (15 or 30 or 60)) Fps = 15;
        if (Height is not (480 or 720 or 1080)) Height = 720;
    }
}

public partial class MainWindow
{
    private UserSettings preferences = new();
    private static string PreferencesPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteDesk", "ayarlar.json");

    private void AyarlariYukle()
    {
        try
        {
            if (System.IO.File.Exists(PreferencesPath))
                preferences = JsonSerializer.Deserialize<UserSettings>(System.IO.File.ReadAllText(PreferencesPath)) ?? new();
            preferences.Normalize();
            AyarlariYaz(preferences);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or JsonException)
        {
            preferences.Normalize();
            AyarDurumText.Text = "Ayar dosyasi okunamadi/kaydedilemedi. Bu oturumda varsayilanlar kullaniliyor.";
        }
        AyarlariGoster();
    }
    private static void AyarlariYaz(UserSettings value)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PreferencesPath)!);
        string temporary = PreferencesPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            System.IO.File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            System.IO.File.Move(temporary, PreferencesPath, true);
        }
        finally { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
    }
    private void AyarlariGoster()
    {
        AktarimCombo.SelectedIndex = preferences.VideoTransport == "webrtc" ? 1 : 0;
        StunTextBox.Text = preferences.StunUrl;
        TurnTextBox.Text = preferences.TurnUrl;
        TurnUserTextBox.Text = preferences.TurnUsername;
        AdaptifGoruntuCheckBox.IsChecked = preferences.AdaptiveImage;
        AnahtarDogrulamaCheckBox.IsChecked = preferences.VerifyPeerKey;
        KimlikAdiTextBox.Text = preferences.DisplayName;
        FpsCombo.SelectedIndex = preferences.Fps == 60 ? 2 : preferences.Fps == 30 ? 1 : 0;
        CozunurlukCombo.SelectedIndex = preferences.Height == 1080 ? 2 : preferences.Height == 720 ? 1 : 0;
        KimlikText.Text = preferences.DisplayName;
    }
    private void AyarlariKaydet_Click(object sender, RoutedEventArgs e)
    {
        var updated = new UserSettings
        {
            VideoTransport = AktarimCombo.SelectedIndex == 1 ? "webrtc" : "jpeg",
            StunUrl = StunTextBox.Text.Trim(), TurnUrl = TurnTextBox.Text.Trim(), TurnUsername = TurnUserTextBox.Text.Trim(),
            AdaptiveImage = AdaptifGoruntuCheckBox.IsChecked == true,
            VerifyPeerKey = AnahtarDogrulamaCheckBox.IsChecked == true,
            DisplayName = KimlikAdiTextBox.Text,
            Fps = int.Parse((string)((ComboBoxItem)FpsCombo.SelectedItem).Tag),
            Height = int.Parse((string)((ComboBoxItem)CozunurlukCombo.SelectedItem).Tag)
        };
        updated.Normalize();
        if (!RtcPolicy.ValidIceUrl(updated.StunUrl, false) || !RtcPolicy.ValidIceUrl(updated.TurnUrl, true))
        { AyarDurumText.Text = "STUN/TURN adresi gecersiz. stun:host:port veya turn:host:port kullanin."; return; }
        try { AyarlariYaz(updated); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        { AyarDurumText.Text = "Ayarlar kaydedilemedi. Dosya izinlerini kontrol edin."; return; }
        preferences = updated;
        rtcTurnPassword = TurnPasswordBox.Password;
        if (rtcView != null && paylasan) rtcView.Post(new { kind = "quality", fps = updated.Fps, height = updated.Height });
        AyarlariGoster();
        AyarDurumText.Text = "Kaydedildi. Goruntu ayarlari mevcut paylasima, aktarim yontemi ve ag ayarlari sonraki oturuma uygulanir. TURN parolasi yalniz bu uygulama acikken tutulur.";
    }
}
