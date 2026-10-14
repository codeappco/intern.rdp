using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Text;
using System.Text.Json;

namespace stajprojesi_uzaktan_baglanti;

// One isolated, packaged page; no remote scripts, navigation or host objects.
internal sealed class RtcView : IDisposable
{
    internal const string PageUrl = "https://remotedesk.invalid/rtc";
    // Composition avoids the native child-window controller initialization failure
    // (0x8007139F) seen with the HWND-based WebView2 on this machine.
    public WebView2CompositionControl Control { get; } = new();
    public event Action<JsonElement>? Message;
    public event Action<string>? Failure;
    private bool disposed;
    internal async Task Initialize(bool host)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteDesk", "WebRtcRuntime");
        var environment = await CoreWebView2Environment.CreateAsync(null, folder).WaitAsync(TimeSpan.FromSeconds(20));
        if (disposed) return;
        await Control.EnsureCoreWebView2Async(environment).WaitAsync(TimeSpan.FromSeconds(20));
        if (disposed) return;
        var core = Control.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.NavigationStarting += (_, e) => { if (e.Uri != PageUrl) e.Cancel = true; };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.ScreenCaptureStarting += (_, e) => e.Cancel = !host || disposed;
        core.ProcessFailed += (_, _) => { if (!disposed) Failure?.Invoke("WebRTC motoru kapandi."); };
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (e.Request.Uri == PageUrl)
            {
                var data = typeof(RtcView).Assembly.GetManifestResourceStream("RemoteDesk.WebRtc.html")!;
                e.Response = environment.CreateWebResourceResponse(data, 200, "OK", "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
            }
            else e.Response = environment.CreateWebResourceResponse(new MemoryStream(), 403, "Forbidden", "");
        };
        core.WebMessageReceived += (_, e) =>
        {
            if (disposed || e.Source != PageUrl || e.WebMessageAsJson.Length > 120000) return;
            try { using var doc = JsonDocument.Parse(e.WebMessageAsJson); Message?.Invoke(doc.RootElement.Clone()); }
            catch (Exception ex) { Failure?.Invoke(ex.Message); }
        };
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        { if (args.IsSuccess) ready.TrySetResult(true); else ready.TrySetException(new InvalidOperationException("WebRTC sayfasi acilamadi.")); }
        core.NavigationCompleted += Completed;
        try { core.Navigate(PageUrl); await ready.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { core.NavigationCompleted -= Completed; }
    }
    public void Post(object value)
    {
        if (!disposed && Control.CoreWebView2 != null)
            Control.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value));
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        // Disposing the WebView also tears down capture tracks, UDP sockets and DTLS keys.
        Control.Dispose();
        Message = null; Failure = null;
    }
}
