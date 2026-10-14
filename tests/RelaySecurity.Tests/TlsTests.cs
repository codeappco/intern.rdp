using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using stajprojesi_uzaktan_baglanti;

static class TlsTests
{
    public static async Task Run()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var issued = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        // Windows Schannel needs an imported server key; a transient RSA handle
        // can fail before presenting a certificate, which would be a false test.
        using var certificate = X509CertificateLoader.LoadPkcs12(issued.Export(X509ContentType.Pfx), null);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            async Task<bool> Serve()
            {
                using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
                using var ssl = new SslStream(peer.GetStream());
                try
                {
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, timeout.Token);
                    return true;
                }
                catch (AuthenticationException) { return false; }
                catch (IOException) { return false; }
            }
            var server = Serve();
            using var client = new ResponsiveWebSocket();
            if (client.Options.RemoteCertificateValidationCallback != null) throw new Exception("Custom TLS validation callback unexpectedly configured");
            bool rejected = false;
            Exception? failure = null;
            try { await client.ConnectAsync(new Uri($"wss://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/ws"), timeout.Token); }
            catch (WebSocketException ex)
            {
                failure = ex;
                for (Exception? cause = ex; cause != null; cause = cause.InnerException)
                    if (cause is AuthenticationException) rejected = true;
            }
            await server;
            if (!rejected) throw new Exception("Expected certificate authentication failure", failure);
            Console.WriteLine("PASS actual desktop transport rejects untrusted TLS certificate without bypass callback");
        }
        finally { listener.Stop(); }
    }
}
