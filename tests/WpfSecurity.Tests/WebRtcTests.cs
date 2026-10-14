using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using stajprojesi_uzaktan_baglanti;

internal static class WebRtcTests
{
    public static async Task Run()
    {
        void Check(bool pass, string name) { if (!pass) throw new Exception(name); Console.WriteLine("PASS " + name); }
        void Reject(Action action, string name) { try { action(); } catch (CryptographicException) { Console.WriteLine("PASS " + name); return; } throw new Exception(name); }
        Check(RtcPolicy.ValidIceUrl("stun:stun.l.google.com:19302", false) && RtcPolicy.ValidIceUrl("turns:relay.example:5349?transport=tcp", true), "valid STUN/TURN settings");
        Check(!RtcPolicy.ValidIceUrl("https://relay.example", true) && !RtcPolicy.ValidIceUrl("turn:host\r\nheader", true), "reject invalid ICE settings");
        Reject(() => RtcPolicy.ValidateSignal(JsonSerializer.SerializeToElement(new { type = "offer", sdp = "m=video 9 RTP/AVP 96\r\na=rtpmap:96 H264/90000" }), false), "reject unencrypted video offer");
        var gate = new PeerVerification("0123456789ABCDEF0123456789ABCDEF");
        Reject(() => gate.DemandSend("rtc_sinyal"), "signalling blocked before secure readiness");
        gate.AcceptRelayTrust(); gate.LocalReadySent(); gate.PeerReady(); gate.DemandSend("rtc_sinyal");
        gate.Close(); Reject(() => gate.DemandReceive("rtc_sinyal"), "signalling blocked after cleanup");
        var order = new RtcInputOrder();
        Check(order.Accept("hareket", 5) && order.Accept("tus", 3), "new pointer update cannot discard an earlier reliable key");
        Check(!order.Accept("hareket", 4) && order.Accept("birak", 6) && !order.Accept("hareket", 5), "stale pointer updates cannot overtake release");
        Check(!order.Accept("tus", 3) && !order.Accept("tus", 0), "duplicate and invalid input sequence rejected");
        order.Reset(); Check(order.Accept("tus", 1), "new session resets ordering");
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var main = new MainWindow { ShowActivated = false, ShowInTaskbar = false };
        try
        {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            ((TextBox)typeof(MainWindow).GetField("SunucuAdresTextBox", flags)!.GetValue(main)!).Text = "";
            typeof(MainWindow).GetField("paylasan", flags)!.SetValue(main, true);
            ((FrameworkElement)typeof(MainWindow).GetField("AktifOturumPanel", flags)!.GetValue(main)!).Visibility = Visibility.Visible;
            ((FrameworkElement)typeof(MainWindow).GetField("BaglantiKartlari", flags)!.GetValue(main)!).Visibility = Visibility.Collapsed;
            using var embedded = new RtcView();
            var embeddedReady = new TaskCompletionSource<bool>();
            embedded.Message += m => { if (m.GetProperty("kind").GetString() == "ready") embeddedReady.TrySetResult(true); };
            try
            {
                if (attempt == 0) main.Show();
                typeof(MainWindow).GetMethod("RtcYerlestir", flags)!.Invoke(main, new object[] { embedded, "smoke" });
                await embedded.Initialize(true);
                embedded.Post(new { kind = "init", host = true, fps = 15, height = 720, iceServers = Array.Empty<object>() });
                await embeddedReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Check(await embedded.Control.CoreWebView2.ExecuteScriptAsync("!share.hidden && !share.disabled && share.getBoundingClientRect().width>0") == "true", "embedded screen share button ready on attempt " + attempt);
            }
            finally { typeof(MainWindow).GetMethod("RtcTemizle", flags)!.Invoke(main, null); }
        }
        }
        finally { main.Close(); }
        using var host = new RtcView(); using var viewer = new RtcView();
        var layout = new Grid(); layout.ColumnDefinitions.Add(new()); layout.ColumnDefinitions.Add(new());
        Grid.SetColumn(viewer.Control, 1); layout.Children.Add(host.Control); layout.Children.Add(viewer.Control);
        var window = new Window { Title = "RemoteDesk WebRTC integration test", Width = 1000, Height = 400, ShowActivated = false, ShowInTaskbar = false, Content = layout };
        using var hostCrypto = new SecureSession("rtc-test", true); using var viewerCrypto = new SecureSession("rtc-test", false);
        hostCrypto.Establish(viewerCrypto.PublicKey); viewerCrypto.Establish(hostCrypto.PublicKey);
        var hostReady = new TaskCompletionSource<bool>(); var viewerReady = new TaskCompletionSource<bool>();
        var received = new TaskCompletionSource<bool>(); var failure = new TaskCompletionSource<bool>();
        int encryptedSignals = 0; bool bothConnected = false; int connected = 0;
        void Message(JsonElement msg, bool fromHost)
        {
            try
            {
                var kind = msg.GetProperty("kind").GetString();
                if (kind == "ready") (fromHost ? hostReady : viewerReady).TrySetResult(true);
                else if (kind == "error") throw new Exception(msg.GetProperty("message").GetString());
                else if (kind == "connected") { connected++; bothConnected = connected >= 2; }
                else if (kind == "signal")
                {
                    var signal = msg.GetProperty("signal"); RtcPolicy.ValidateSignal(signal, !fromHost);
                    var encrypted = JsonSerializer.SerializeToElement((fromHost ? hostCrypto : viewerCrypto).Encrypt(new { tip = "rtc_sinyal", oturumId = "rtc-test", signal }));
                    using var plain = (fromHost ? viewerCrypto : hostCrypto).Decrypt(encrypted);
                    (fromHost ? viewer : host).Post(new { kind = "signal", signal = plain.RootElement.GetProperty("signal") });
                    encryptedSignals++;
                }
                else if (kind == "stats" && !fromHost && msg.GetProperty("fps").GetDouble() > 0 && msg.GetProperty("codec").GetString() == "video/H264") received.TrySetResult(true);
            }
            catch (Exception ex) { failure.TrySetException(ex); }
        }
        host.Message += m => Message(m, true); viewer.Message += m => Message(m, false);
        host.Failure += e => failure.TrySetException(new Exception(e)); viewer.Failure += e => failure.TrySetException(new Exception(e));
        try
        {
            window.Show(); await host.Initialize(true); await viewer.Initialize(false);
            Check(await host.Control.CoreWebView2.ExecuteScriptAsync("typeof navigator.mediaDevices.getDisplayMedia === 'function'") == "true", "screen capture API available in WebView2");
            host.Post(new { kind = "init", host = true, fps = 15, height = 480, iceServers = Array.Empty<object>() });
            viewer.Post(new { kind = "init", host = false, fps = 15, height = 480, iceServers = Array.Empty<object>() });
            await (await Task.WhenAny(Task.WhenAll(hostReady.Task, viewerReady.Task), failure.Task)).WaitAsync(TimeSpan.FromSeconds(20));
            // Synthetic source exercises the real H.264 encoder/decoder without capturing the user's desktop.
            await host.Control.CoreWebView2.ExecuteScriptAsync("(()=>{const canvas=document.createElement('canvas');canvas.width=640;canvas.height=360;const ctx=canvas.getContext('2d');let n=0;const draw=setInterval(()=>{ctx.fillStyle=n++%2?'#315de6':'#179455';ctx.fillRect(0,0,640,360);ctx.fillStyle='white';ctx.font='32px sans-serif';ctx.fillText('Encrypted H.264 test '+n,30,100);if(stopped)clearInterval(draw);},66);publishStream(canvas.captureStream(15)).catch(fail);})()");
            var result = await Task.WhenAny(received.Task, failure.Task, Task.Delay(35000));
            if (result == failure.Task) await failure.Task;
            Check(result == received.Task && bothConnected, "real WebView2 peers encode and decode H.264 frames over WebRTC");
            Check(encryptedSignals >= 2, "SDP and ICE pass through AES-GCM encrypted signalling");
            // ExecuteScriptAsync does not await JavaScript promises. Report via the existing bridge instead.
            var dtls = new TaskCompletionSource<bool>();
            viewer.Message += m => { if (m.GetProperty("kind").GetString() == "test-dtls") dtls.TrySetResult(m.GetProperty("secure").GetBoolean()); };
            await viewer.Control.CoreWebView2.ExecuteScriptAsync("pc.getStats().then(s=>send({kind:'test-dtls',secure:Array.from(s.values()).some(v=>v.type==='transport'&&v.dtlsState==='connected')})).catch(fail)");
            Check(await dtls.Task.WaitAsync(TimeSpan.FromSeconds(5)), "DTLS transport connected");
            var qualityApplied = new TaskCompletionSource<JsonElement>();
            host.Message += m => { if (m.GetProperty("kind").GetString() == "test-quality") qualityApplied.TrySetResult(m.Clone()); };
            host.Post(new { kind = "quality", fps = 30, height = 720 });
            await host.Control.CoreWebView2.ExecuteScriptAsync("operation.then(()=>{const p=pc.getSenders().find(s=>s.track?.kind==='video').getParameters();const c=stream.getVideoTracks()[0].getConstraints();send({kind:'test-quality',fps:p.encodings[0].maxFramerate,noBitrate:p.encodings[0].maxBitrate===undefined,preference:p.degradationPreference,ideal:c.frameRate.ideal,noMinimum:c.frameRate.min===undefined,height:c.height.max});}).catch(fail)");
            var applied = await qualityApplied.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(applied.GetProperty("fps").GetInt32() == 30 && applied.GetProperty("ideal").GetInt32() == 30 && applied.GetProperty("height").GetInt32() == 720,
                "live settings update H.264 target/max FPS and resolution");
            Check(applied.GetProperty("noBitrate").GetBoolean() && applied.GetProperty("noMinimum").GetBoolean(),
                "H.264 has no application bitrate ceiling or minimum FPS");
            Check(applied.GetProperty("preference").GetString() == "maintain-resolution", "H.264 prioritizes selected resolution");
            var grantReceived = new TaskCompletionSource<bool>();
            var keyReceived = new TaskCompletionSource<bool>();
            var moveReceived = new TaskCompletionSource<bool>();
            viewer.Message += m => { if (m.GetProperty("kind").GetString() == "data" && m.GetProperty("data").GetProperty("tip").GetString() == "kontrol_izni") grantReceived.TrySetResult(m.GetProperty("data").GetProperty("izin").GetString() == "test-lease"); };
            host.Message += m => { if (m.GetProperty("kind").GetString() == "data") { var d=m.GetProperty("data"); if(d.GetProperty("eylem").GetString()=="hareket") moveReceived.TrySetResult(d.GetProperty("sira").GetInt64()>0); else keyReceived.TrySetResult(d.GetProperty("tus").GetInt32()==65 && d.GetProperty("izin").GetString()=="test-lease"); } };
            host.Post(new { kind = "send-data", data = new { tip = "kontrol_izni", oturumId = "rtc-test", izin = "test-lease" } });
            Check(await grantReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)), "permission delivered over encrypted DataChannel");
            viewer.Post(new { kind = "send-data", data = new { tip = "girdi", oturumId = "rtc-test", izin = "test-lease", eylem = "tus", tus = 65, basili = true } });
            viewer.Post(new { kind = "send-data", data = new { tip = "girdi", oturumId = "rtc-test", izin = "test-lease", eylem = "hareket", x = .5, y = .5 } });
            Check(await keyReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)), "reliable key delivered over DataChannel without relay signalling");
            Check(await moveReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)), "pointer delivered over separate unordered DataChannel");
            Check(await host.Control.CoreWebView2.ExecuteScriptAsync("controlChannel.ordered && controlChannel.maxRetransmits===null && !moveChannel.ordered && moveChannel.maxRetransmits===0") == "true", "key and pointer channels use intended reliability policies");
            int inputs = 0;
            viewer.Message += m => { if (m.GetProperty("kind").GetString() == "input") inputs++; };
            await viewer.Control.CoreWebView2.ExecuteScriptAsync("input('tus',{tus:65,basili:true})");
            await Task.Delay(100);
            Check(inputs == 0, "WebRTC viewer emits no input before permission");
            viewer.Post(new { kind = "permission", allowed = true });
            await Task.Delay(100);
            await viewer.Control.CoreWebView2.ExecuteScriptAsync("input('tus',{tus:65,basili:true})");
            await Task.Delay(100);
            Check(inputs == 1, "WebRTC viewer emits input after permission");
            viewer.Post(new { kind = "permission", allowed = false });
            await Task.Delay(100);
            await viewer.Control.CoreWebView2.ExecuteScriptAsync("input('tus',{tus:65,basili:true})");
            await Task.Delay(100);
            Check(inputs == 1, "WebRTC viewer stops input after revocation");
            host.Post(new { kind = "stop" }); viewer.Post(new { kind = "stop" });
            await Task.Delay(200);
            Check(await host.Control.CoreWebView2.ExecuteScriptAsync("pc.connectionState==='closed' && stream.getTracks().every(t=>t.readyState==='ended')") == "true", "stop closes WebRTC and ends all source tracks");
        }
        finally { window.Content = null; window.Close(); }
    }
}
