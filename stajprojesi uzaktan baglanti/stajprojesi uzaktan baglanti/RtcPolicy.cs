using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace stajprojesi_uzaktan_baglanti;

internal static class RtcPolicy
{
    public static bool ValidIceUrl(string value, bool turn) => value.Length == 0 ||
        (value.Length <= 512 && Regex.IsMatch(value,
            turn ? @"^turns?:[A-Za-z0-9.\-\[\]:]+(?:\?transport=(?:udp|tcp))?$" : @"^stuns?:[A-Za-z0-9.\-\[\]:]+$"));

    public static void ValidateSignal(JsonElement signal, bool receivingAsHost)
    {
        if (signal.GetRawText().Length > 100000) throw new CryptographicException("WebRTC mesaji cok buyuk.");
        string? type = signal.GetProperty("type").GetString();
        if (type == "candidate")
        {
            var candidate = signal.GetProperty("candidate");
            string? text = candidate.GetProperty("candidate").GetString();
            if (text == null || text.Length > 4096 || !text.StartsWith("candidate:") || text.Contains('\n') || text.Contains('\r'))
                throw new CryptographicException("Gecersiz ICE adayi.");
            return;
        }
        if (type != (receivingAsHost ? "answer" : "offer")) throw new CryptographicException("WebRTC teklif rolu gecersiz.");
        string sdp = signal.GetProperty("sdp").GetString() ?? "";
        if (!Regex.IsMatch(sdp, @"(?m)^a=fingerprint:sha-256 (?:[0-9A-Fa-f]{2}:){31}[0-9A-Fa-f]{2}\r?$") ||
            !sdp.Contains("UDP/TLS/RTP/SAVPF") || !sdp.Contains("H264/90000") ||
            Regex.Matches(sdp, @"(?m)^m=").Count != 2 ||
            Regex.Matches(sdp, @"(?m)^m=video ").Count != 1 ||
            !Regex.IsMatch(sdp, @"(?m)^m=application [0-9]+ UDP/DTLS/SCTP webrtc-datachannel\r?$"))
            throw new CryptographicException("Sifreli H.264 video teklifi gerekli.");
    }
}
