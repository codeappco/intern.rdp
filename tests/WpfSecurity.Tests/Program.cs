using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using stajprojesi_uzaktan_baglanti;

static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool pass, string name) { if (!pass) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static object? Get(MainWindow w, string name) => typeof(MainWindow).GetField(name, Private)!.GetValue(w);
    static void Set(MainWindow w, string name, object? value) => typeof(MainWindow).GetField(name, Private)!.SetValue(w, value);
    static object? Call(MainWindow w, string name, params object[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(w, args);
    static void Await(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var dispatcher = Dispatcher.CurrentDispatcher;
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
    [STAThread]
    static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            if (Environment.GetEnvironmentVariable("REMOTEDESK_RTC_TEST") == "1") { app.Startup += async (_, _) => { try { await WebRtcTests.Run(); app.Shutdown(0); } catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); } }; return app.Run(); }
            InputTests.Run();
            var adaptive = new StreamImageProfile();
            adaptive.Configure(1080, true);
            for (int i = 0; i < 240; i++) adaptive.Observe(153000, 153, 37, 30);
            Check(adaptive.Height == 480 && adaptive.Quality == 25, "sustained overload reduces image cost to bounded floor");
            for (int i = 0; i < 600; i++) adaptive.Observe(10000, 1, 1, 30);
            Check(adaptive.Height == 1080 && adaptive.Quality == 40, "sustained spare capacity restores configured resolution");
            adaptive.Configure(720, false);
            for (int i = 0; i < 240; i++) adaptive.Observe(153000, 153, 37, 60);
            Check(adaptive.Height == 720 && adaptive.Quality == 60, "disabled adaptation preserves chosen resolution and original quality");
            adaptive.Configure(480, true);
            for (int i = 0; i < 600; i++) adaptive.Observe(10000, 1, 1, 15);
            Check(adaptive.Height == 480, "adaptation never exceeds user's resolution");

            var owner = new Window { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            owner.Show();
            const string fullCode = "0123-4567-89AB-CDEF-0123-4567-89AB-CDEF";
            var approved = new PeerVerification(fullCode);
            var positive = new PeerVerificationDialog(owner, fullCode, approved) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            positive.Loaded += (_, _) =>
            {
                var panel = (StackPanel)positive.Content;
                panel.Children.OfType<TextBox>().Single(t => !t.IsReadOnly).Text = fullCode;
                panel.Children.OfType<StackPanel>().Single().Children.OfType<Button>().Single(b => !b.IsCancel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            Check(positive.ShowDialog() == true && !approved.IsActive, "correct code in real dialog permits only local readiness, not screen/control");
            approved.LocalReadySent(); approved.PeerReady();
            Check(approved.IsActive, "correct comparison and encrypted peer confirmation allow activation");
            owner.Close();
            foreach (string decision in new[] { "wrong", "close", "timeout" })
            {
                var window = new MainWindow { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                ((UserSettings)Get(window, "preferences")!).VerifyPeerKey = true;
                window.Show();
                using var peer = new SecureSession("wpf", false);
                Set(window, "secureSession", new SecureSession("wpf", true));
                Set(window, "oturumId", "wpf"); Set(window, "paylasan", true);
                Exception? failure = null;
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    var dialog = app.Windows.OfType<PeerVerificationDialog>().Single();
                    // Exercise the actual WPF Click handler without showing a test window.
                    dialog.Opacity = 0; dialog.ShowInTaskbar = false;
                    try
                    {
                        var panel = (StackPanel)dialog.Content;
                        Check(panel.Children.OfType<TextBlock>().Any(t => t.Text == PeerVerification.Instructions), "real dialog displays independent trusted-channel instructions");
                        var buttons = panel.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToArray();
                        Check(buttons.Any(b => b.IsCancel) && buttons.All(b => !b.IsDefault), "dialog has cancellation and no default affirmative action");
                        Check(Get(window, "paylasimIptal") == null && Get(window, "ekranPenceresi") == null, "real WPF flow has not started screen sharing or viewer before verification");
                        if (decision == "wrong")
                        {
                            panel.Children.OfType<TextBox>().Single(t => !t.IsReadOnly).Text = "wrong-code";
                            buttons.Single(b => !b.IsCancel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        else if (decision == "timeout") Call(window, "OturumuTemizle");
                        else dialog.Close();
                    }
                    catch (Exception ex) { failure = ex; dialog.Close(); }
                }));
                var envelope = JsonSerializer.SerializeToElement(new { tip = "e2ee_anahtar", oturumId = "wpf", surum = 1, anahtar = peer.PublicKey });
                Await((Task)Call(window, "SecureMessage", envelope)!);
                if (failure != null) throw failure;
                Check(Get(window, "oturumId") == null && Get(window, "secureSession") == null && Get(window, "peerVerification") == null,
                    "real WPF " + decision + " clears session and cryptographic verification");
                Check((string)Get(window, "kontrolIzni")! == "" && Get(window, "paylasimIptal") == null, "rejection leaves no input permission or screen task");
                window.Close();
            }
            var embedded = new MainWindow();
            using (var embeddedView = new RtcView())
            {
                ((TabControl)Get(embedded, "AnaSekmeler")!).SelectedIndex = 1;
                Set(embedded, "paylasan", true);
                Set(embedded, "rtcView", embeddedView);
                Call(embedded, "RtcYerlestir", embeddedView, "embedded-test");
                Check(((TabControl)Get(embedded, "AnaSekmeler")!).SelectedIndex == 0, "screen sharing switches from settings to visible connection tab");
                var area = (ContentControl)Get(embedded, "RtcPaylasimAlani")!;
                Check(ReferenceEquals(area.Content, embeddedView.Control) && area.Visibility == Visibility.Visible && Get(embedded, "rtcWindow") == null,
                    "host screen selection embeds in main window without an extra window");
                Call(embedded, "RtcTemizle");
                Check(area.Content == null && area.Visibility == Visibility.Collapsed,
                    "session cleanup detaches embedded screen sharing control");
            }
            embedded.Close();
            var guarded = new MainWindow();
            using var crypto = new SecureSession("guard", true);
            Set(guarded, "secureSession", crypto);
            var gate = new PeerVerification("0123-4567-89AB-CDEF-0123-4567-89AB-CDEF");
            Set(guarded, "peerVerification", gate);
            gate.ConfirmPeerCode("0123456789ABCDEF0123456789ABCDEF");
            gate.LocalReadySent();
            foreach (string type in new[] { "ekran", "girdi", "kontrol_izni" })
            {
                try { Await((Task)Call(guarded, "SecureSend", new { tip = type })!); throw new Exception("Send bypass: " + type); }
                catch (CryptographicException ex) { Check(ex.Message.Contains("dogrulamasi"), "actual SecureSend blocks local-only " + type); }
            }
            Set(guarded, "kodBitis", Environment.TickCount64 + 65000L);
            Call(guarded, "KodSuresiniGuncelle");
            Check(((TextBlock)Get(guarded, "KodSureText")!).Text.StartsWith("Kalan sure: 01:"), "code countdown uses deadline");
            Set(guarded, "kodBitis", Environment.TickCount64 - 1L);
            Call(guarded, "KodSuresiniGuncelle");
            Check(((TextBlock)Get(guarded, "KendiKodumTextBlock")!).Text.Contains("suresi doldu"), "expired code cleared");
            Call(guarded, "OturumuTemizle");
            Check(((System.Windows.Controls.Grid)Get(guarded, "BaglantiKartlari")!).Visibility == Visibility.Visible &&
                ((System.Windows.Controls.Border)Get(guarded, "AktifOturumPanel")!).Visibility == Visibility.Collapsed,
                "session cleanup restores connection cards");
            guarded.Close();
            var pipeline = new MainWindow();
            using var senderCrypto = new SecureSession("pipeline", true);
            using var receiverCrypto = new SecureSession("pipeline", false);
            senderCrypto.Establish(receiverCrypto.PublicKey); receiverCrypto.Establish(senderCrypto.PublicKey);
            var pipelineGate = new PeerVerification(senderCrypto.VerificationCode!);
            pipelineGate.AcceptRelayTrust(); pipelineGate.LocalReadySent(); pipelineGate.PeerReady();
            Set(pipeline, "secureSession", senderCrypto); Set(pipeline, "peerVerification", pipelineGate);
            Set(pipeline, "oturumId", "pipeline"); Set(pipeline, "paylasan", true);
            Set(pipeline, "bekleyenKareSayisi", 10L);
            var ackMessage = JsonSerializer.SerializeToElement(receiverCrypto.Encrypt(new { tip = "ekran_alindi", oturumId = "pipeline", gonderimZamani = Environment.TickCount64 - 100 }));
            Await((Task)Call(pipeline, "SecureMessage", ackMessage)!);
            Check((long)Get(pipeline, "bekleyenKareSayisi")! == 9L,
                "acknowledgement counts delivered frames beyond former three-frame limit");
            Check((double)Get(pipeline, "kareTurMs")! >= 100, "echoed timestamp measures frame round trip");
            var legacyAck = JsonSerializer.SerializeToElement(receiverCrypto.Encrypt(new { tip = "ekran_alindi", oturumId = "pipeline" }));
            Await((Task)Call(pipeline, "SecureMessage", legacyAck)!);
            Check((long)Get(pipeline, "bekleyenKareSayisi")! == 8L, "legacy acknowledgement remains compatible");
            Call(pipeline, "OturumuTemizle");
            Check((long)Get(pipeline, "bekleyenKareSayisi")! == 0L, "disconnect resets frame telemetry");
            pipeline.Close();
            app.Shutdown();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(); return 1; }
    }
}


