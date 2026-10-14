using System.Security.Cryptography;
using stajprojesi_uzaktan_baglanti;

static class VerificationTests
{
    public static void Run()
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
        void Reject(Action action, string name)
        {
            try { action(); } catch (CryptographicException) { Console.WriteLine("PASS " + name); return; }
            throw new Exception(name);
        }
        var relayTrust = new PeerVerification("0123456789ABCDEF0123456789ABCDEF");
        relayTrust.AcceptRelayTrust();
        Check(!relayTrust.IsActive, "relay trust still requires encrypted peer readiness");
        relayTrust.LocalReadySent(); relayTrust.PeerReady();
        Check(relayTrust.IsActive, "relay trust activates after both ready");
        relayTrust.DemandSend("ekran_alindi");
        relayTrust.Close();
        Reject(() => relayTrust.AcceptRelayTrust(), "closed relay trust cannot reactivate");
        const string code = "0123-4567-89AB-CDEF-0123-4567-89AB-CDEF";
        foreach (var type in new[] { "ekran", "girdi", "kontrol_izni", "rtc_sinyal" })
        {
            var gate = new PeerVerification(code);
            Reject(() => gate.DemandSend(type), "no unverified outgoing " + type);
            Reject(() => gate.DemandReceive(type), "no unverified incoming " + type);
            gate.PeerReady();
            Check(!gate.IsActive, "peer readiness cannot auto-approve local user");
            Reject(() => gate.DemandSend(type), "peer-only approval blocks " + type);
            Check(gate.ConfirmPeerCode(code.ToLowerInvariant()), "full independent code accepted");
            Reject(() => gate.DemandSend(type), "local readiness must be sent before " + type);
            gate.LocalReadySent();
            Check(gate.IsActive, "both verified sides activate session");
            gate.DemandSend(type); gate.DemandReceive(type);
            Reject(gate.PeerReady, "duplicate encrypted readiness rejected");
            Reject(() => gate.DemandSend("e2ee_hazir"), "readiness cannot be sent twice");
            gate.Close();
            Reject(() => gate.DemandSend(type), "cleanup revokes " + type);
            Check(!gate.ConfirmPeerCode(code), "closed session cannot be reapproved");
        }
        foreach (var input in new string?[] { null, "", "0123", code[..^1] + "0", new string('G', 32) })
        {
            var gate = new PeerVerification(code);
            Check(!gate.ConfirmPeerCode(input) && !gate.IsActive, "missing or wrong comparison fails closed");
            Check(!gate.ConfirmPeerCode(code), "wrong-code session cannot recover by later approval");
        }
        var localOnly = new PeerVerification(code);
        localOnly.ConfirmPeerCode(code); localOnly.LocalReadySent();
        Check(!localOnly.IsActive, "local-only verification cannot start sharing");
        Reject(() => localOnly.DemandSend("ekran"), "waiting for encrypted peer confirmation");

        // Two real attacker exchanges, not a mocked verification-code mismatch.
        using var host = new SecureSession("mitm", true);
        using var viewer = new SecureSession("mitm", false);
        using var toHost = new SecureSession("mitm", false);
        using var toViewer = new SecureSession("mitm", true);
        host.Establish(toHost.PublicKey); toHost.Establish(host.PublicKey);
        viewer.Establish(toViewer.PublicKey); toViewer.Establish(viewer.PublicKey);
        Check(host.VerificationCode != viewer.VerificationCode, "MITM split ECDH exchanges show different full codes");
        var h = new PeerVerification(host.VerificationCode!);
        var v = new PeerVerification(viewer.VerificationCode!);
        h.PeerReady(); v.PeerReady(); // Even authenticated attacker readiness is insufficient.
        Check(!h.ConfirmPeerCode(viewer.VerificationCode) && !v.ConfirmPeerCode(host.VerificationCode), "independent code comparison rejects MITM on both endpoints");
        Reject(() => h.DemandSend("ekran"), "MITM cannot trigger screen sharing before comparison");
        Reject(() => v.DemandSend("girdi"), "MITM cannot trigger input before comparison");
    }
}
