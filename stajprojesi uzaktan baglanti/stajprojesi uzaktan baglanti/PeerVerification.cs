using System.Security.Cryptography;
using System.Text;

namespace stajprojesi_uzaktan_baglanti;

// Session readiness gate; manual verification is an optional client policy.
public sealed class PeerVerification(string verificationCode)
{
    public const string Instructions = "Karsi bilgisayarin ekranindaki kodun TAMAMINI, kimligini bildiginiz kisiyle telefon veya yuz yuze gibi RemoteDesk'ten bagimsiz ve guvenilir bir kanaldan alin. Asagiya karsi tarafin kodunu yazin. Bu penceredeki kendi kodunuzu kopyalamayin. Kod farkliysa veya bagimsiz karsilastirma yapamiyorsaniz Iptal secin. Dogrulama bitmeden ekran, fare ve klavye verisi paylasilmaz.";
    private readonly string expected = Normalize(verificationCode);
    private bool local, peer, sent, closed;
    public bool IsActive => !closed && local && peer && sent;

    private static string Normalize(string value) => new(value.Where(c => c != '-' && !char.IsWhiteSpace(c)).Select(char.ToUpperInvariant).ToArray());
    public bool ConfirmPeerCode(string? independentlyObtainedCode)
    {
        if (closed || local) return false;
        string actual = Normalize(independentlyObtainedCode ?? "");
        if (expected.Length != 32 || actual.Length != 32 ||
            actual.Any(c => !Uri.IsHexDigit(c)) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual)))
        { Close(); return false; }
        local = true;
        return true;
    }
    public void AcceptRelayTrust()
    {
        if (closed || local) throw new CryptographicException("Oturum kapali veya zaten onaylandi.");
        local = true;
    }
    public void DemandSend(string? type)
    {
        if (closed || (type == "e2ee_hazir" ? !local || sent : !IsActive || type is not ("ekran" or "ekran_alindi" or "girdi" or "kontrol_izni" or "rtc_sinyal")))
            throw new CryptographicException("Bagimsiz anahtar dogrulamasi tamamlanmadi.");
    }
    public void LocalReadySent() { DemandSend("e2ee_hazir"); sent = true; }
    public void PeerReady()
    {
        if (closed || peer) throw new CryptographicException("Gecersiz veya tekrar dogrulama.");
        peer = true;
    }
    public void DemandReceive(string? type)
    {
        if (!IsActive || type is not ("ekran" or "ekran_alindi" or "girdi" or "kontrol_izni" or "rtc_sinyal"))
            throw new CryptographicException("Dogrulamadan veri alinmaz.");
    }
    public void Close() { closed = true; local = peer = sent = false; }
}
