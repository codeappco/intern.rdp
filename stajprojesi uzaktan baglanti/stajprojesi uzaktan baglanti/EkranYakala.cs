using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace stajprojesi_uzaktan_baglanti;

internal static class EkranYakala
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr dc, int index);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);

    // Birincil ekran; secilen sinirlar icinde en-boy orani korunur.
    public static byte[] Jpeg(int height = 720, int jpegQuality = 60)
    {
        if (jpegQuality < 15 || jpegQuality > 90) throw new ArgumentOutOfRangeException(nameof(jpegQuality));
        var ekran = GetDC(IntPtr.Zero);
        var bellek = CreateCompatibleDC(ekran);
        var bitmap = IntPtr.Zero;
        var onceki = IntPtr.Zero;
        try
        {
            int genislik = GetDeviceCaps(ekran, 118), yukseklik = GetDeviceCaps(ekran, 117);
            if (ekran == IntPtr.Zero || bellek == IntPtr.Zero || genislik <= 0 || yukseklik <= 0)
                throw new InvalidOperationException("Ekran yakalanamadi.");
            bitmap = CreateCompatibleBitmap(ekran, genislik, yukseklik);
            if (bitmap == IntPtr.Zero) throw new InvalidOperationException("Ekran belleği ayrilamadi.");
            onceki = SelectObject(bellek, bitmap);
            if (!BitBlt(bellek, 0, 0, genislik, yukseklik, ekran, 0, 0, 0x40CC0020))
                throw new InvalidOperationException("Ekran yakalanamadi.");
            BitmapSource kaynak = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            int width = height switch { 480 => 854, 720 => 1280, 1080 => 1920, _ => throw new ArgumentOutOfRangeException(nameof(height)) };
            double oran = Math.Min(1, Math.Min((double)width / genislik, (double)height / yukseklik));
            if (oran < 1) kaynak = new TransformedBitmap(kaynak, new ScaleTransform(oran, oran));
            // Leave room for Base64 and JSON inside the encrypted payload limit.
            foreach (int quality in new[] { jpegQuality, Math.Max(15, jpegQuality - 15), 15 }.Distinct())
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = quality };
                encoder.Frames.Add(BitmapFrame.Create(kaynak));
                using var veri = new MemoryStream();
                try
                {
                    encoder.Save(veri);
                    if (veri.Length <= 1000000) return veri.ToArray();
                }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(veri.GetBuffer().AsSpan(0, (int)veri.Length)); }
            }
            throw new InvalidOperationException("Goruntu cok buyuk. Daha dusuk cozunurluk secin.");
        }
        finally
        {
            if (onceki != IntPtr.Zero) SelectObject(bellek, onceki);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (bellek != IntPtr.Zero) DeleteDC(bellek);
            if (ekran != IntPtr.Zero) ReleaseDC(IntPtr.Zero, ekran);
        }
    }
}
