namespace stajprojesi_uzaktan_baglanti;

// Adjust image cost, never the FPS selected by the user. Decisions use windows
// rather than a single expensive frame; recovery requires sustained spare capacity.
internal sealed class StreamImageProfile
{
    private int configuredHeight, samples, healthyWindows;
    private bool enabled;
    private double totalBytes, totalSendMs, totalCaptureMs;
    public int Height { get; private set; }
    public int Quality { get; private set; }
    public void Configure(int height, bool adaptive)
    {
        if (configuredHeight == height && enabled == adaptive && Height != 0) return;
        configuredHeight = height; enabled = adaptive; Height = height;
        Quality = adaptive ? 40 : 60;
        ResetWindow(); healthyWindows = 0;
    }
    public void Observe(int bytes, double sendMs, double captureMs, int fps)
    {
        if (!enabled) return;
        totalBytes += bytes; totalSendMs += sendMs; totalCaptureMs += captureMs;
        if (++samples < 12) return;
        double budget = 1000.0 / fps;
        bool overloaded = totalBytes / samples > 64000 || totalSendMs / samples > budget || totalCaptureMs / samples > budget;
        bool spare = totalBytes / samples < 32000 && totalSendMs / samples < budget * .4 && totalCaptureMs / samples < budget * .4;
        if (overloaded)
        {
            healthyWindows = 0;
            if (Quality > 25) Quality -= 5;
            else if (Height > 480) { Height = Height > 720 ? 720 : 480; Quality = 35; }
        }
        else if (spare && ++healthyWindows >= 8)
        {
            healthyWindows = 0;
            if (Quality < 40) Quality += 5;
            else if (Height < configuredHeight) Height = Math.Min(configuredHeight, Height == 480 ? 720 : 1080);
        }
        else if (!spare) healthyWindows = 0;
        ResetWindow();
    }
    private void ResetWindow() { samples = 0; totalBytes = totalSendMs = totalCaptureMs = 0; }
}
