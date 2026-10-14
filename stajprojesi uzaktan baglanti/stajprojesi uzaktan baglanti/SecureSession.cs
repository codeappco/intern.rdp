using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace stajprojesi_uzaktan_baglanti;

// One instance per session. Access is serialized by the client's send lock/UI thread.
public sealed class SecureSession : IDisposable
{
    private readonly ECDiffieHellman exchange = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    private readonly string id;
    private readonly bool host;
    private AesGcm? send, receive;
    private ulong sent, received;
    private bool disposed;
    public string PublicKey { get; }
    public string? VerificationCode { get; private set; }
    public SecureSession(string id, bool host)
    {
        this.id = id; this.host = host;
        PublicKey = Convert.ToBase64String(exchange.ExportSubjectPublicKeyInfo());
    }
    public void Establish(string publicKey)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (send != null || publicKey.Length > 256) throw new CryptographicException("Invalid key exchange.");
        using var peer = ECDiffieHellman.Create();
        var encoded = Convert.FromBase64String(publicKey);
        peer.ImportSubjectPublicKeyInfo(encoded, out int consumed);
        if (consumed != encoded.Length || peer.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
            throw new CryptographicException("Expected P-256 key.");
        var transcript = JsonSerializer.SerializeToUtf8Bytes(new { protocol = "RemoteDesk-E2EE-v1", id,
            hostKey = host ? PublicKey : publicKey, viewerKey = host ? publicKey : PublicKey });
        var salt = SHA256.HashData(transcript);
        var secret = exchange.DeriveRawSecretAgreement(peer.PublicKey);
        byte[]? material = null;
        try
        {
            material = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 80, salt, Encoding.UTF8.GetBytes("RemoteDesk-E2EE-v1 keys"));
            send = new AesGcm(material.AsSpan(host ? 0 : 32, 32), 16);
            receive = new AesGcm(material.AsSpan(host ? 32 : 0, 32), 16);
            // 128-bit comparison avoids the grinding risk of a short, uncommitted SAS.
            string hex = Convert.ToHexString(material.AsSpan(64, 16));
            VerificationCode = string.Join("-", Enumerable.Range(0, 8).Select(i => hex.Substring(i * 4, 4)));
        }
        finally { CryptographicOperations.ZeroMemory(secret); if (material != null) CryptographicOperations.ZeroMemory(material); exchange.Dispose(); }
    }
    private byte[] Aad(ulong sequence, bool fromHost) => JsonSerializer.SerializeToUtf8Bytes(new
        { protocol = "RemoteDesk-E2EE-v1", id, fromHost, sequence });
    private static byte[] Nonce(ulong sequence)
    {
        var nonce = new byte[12]; BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), sequence); return nonce;
    }
    public object Encrypt(object value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (send == null) throw new CryptographicException("Key exchange incomplete.");
        ulong sequence = checked(sent + 1);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(value);
        if (plain.Length > 1400000)
        {
            CryptographicOperations.ZeroMemory(plain);
            throw new CryptographicException("Payload too large.");
        }
        var cipher = new byte[plain.Length]; var tag = new byte[16];
        try { send.Encrypt(Nonce(sequence), plain, cipher, tag, Aad(sequence, host)); sent = sequence; }
        finally { CryptographicOperations.ZeroMemory(plain); }
        return new { tip = "sifreli", oturumId = id, surum = 1, sira = sequence,
            veri = Convert.ToBase64String(cipher), etiket = Convert.ToBase64String(tag) };
    }
    public JsonDocument Decrypt(JsonElement envelope)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (receive == null || envelope.GetProperty("surum").GetInt32() != 1 || envelope.GetProperty("oturumId").GetString() != id)
            throw new CryptographicException("Invalid session.");
        ulong sequence = envelope.GetProperty("sira").GetUInt64();
        if (sequence != checked(received + 1)) throw new CryptographicException("Replay or out-of-order message.");
        var cipher = Convert.FromBase64String(envelope.GetProperty("veri").GetString()!);
        var tag = Convert.FromBase64String(envelope.GetProperty("etiket").GetString()!);
        if (cipher.Length > 1400000 || tag.Length != 16) throw new CryptographicException("Invalid payload.");
        var plain = new byte[cipher.Length];
        try
        {
            receive.Decrypt(Nonce(sequence), cipher, tag, plain, Aad(sequence, !host));
            // Stream parsing copies into document-owned storage without an immutable
            // plaintext string. The temporary source can then be cleared immediately.
            using var stream = new System.IO.MemoryStream(plain, writable: false);
            var document = JsonDocument.Parse(stream);
            received = sequence; return document;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Dispose()
    {
        disposed = true; send?.Dispose(); receive?.Dispose(); exchange.Dispose(); VerificationCode = null;
    }
}
