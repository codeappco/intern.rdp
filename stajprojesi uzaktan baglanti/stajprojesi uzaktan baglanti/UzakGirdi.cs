using System.Runtime.InteropServices;
using System.Text.Json;

namespace stajprojesi_uzaktan_baglanti;

internal sealed class UzakGirdi
{
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int x, y; public uint data, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort key, scan; public uint flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] private struct Payload { [FieldOffset(0)] public Mouse mouse; [FieldOffset(0)] public Keyboard keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint type; public Payload payload; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetMessageExtraInfo();
    private const uint Marker = 0x52444B31;
    public static bool IsInjected => unchecked((uint)GetMessageExtraInfo().ToInt64()) == Marker;
    private readonly HashSet<int> keys = new();
    private readonly HashSet<int> buttons = new();
    private static void Send(Input input)
    {
        if (input.type == 0) input.payload.mouse.extra = new UIntPtr(Marker);
        else input.payload.keyboard.extra = new UIntPtr(Marker);
        if (SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) != 1)
            throw new InvalidOperationException("Windows girdiyi uygulayamadi. Yonetici pencereleri kontrol edilemeyebilir.");
    }
    private static void MouseEvent(uint flags, int x = 0, int y = 0, uint data = 0) => Send(new Input { payload = new Payload { mouse = new Mouse { flags = flags, x = x, y = y, data = data } } });
    private static bool Extended(int key) => key is 33 or 34 or 35 or 36 or 37 or 38 or 39 or 40 or 45 or 46 or 91 or 92 or 111 or 163 or 165;
    private static void KeyEvent(int key, bool down) => Send(new Input { type = 1, payload = new Payload { keyboard = new Keyboard { key = (ushort)key, flags = (down ? 0u : 2u) | (Extended(key) ? 1u : 0u) } } });
    public void Apply(JsonElement message)
    {
        string? action = message.GetProperty("eylem").GetString();
        if (action == "birak") { Release(); return; }
        if (action == "tus")
        {
            int key = message.GetProperty("tus").GetInt32();
            bool down = message.GetProperty("basili").GetBoolean();
            if (key < 8 || key > 254) return;
            if (down) { KeyEvent(key, true); keys.Add(key); }
            else if (keys.Contains(key)) { KeyEvent(key, false); keys.Remove(key); }
            return;
        }
        if (action is not ("hareket" or "dugme" or "tekerlek")) return;
        double x = message.GetProperty("x").GetDouble(), y = message.GetProperty("y").GetDouble();
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || x > 1 || y < 0 || y > 1) return;
        MouseEvent(0x8001, (int)Math.Round(x * 65535), (int)Math.Round(y * 65535));
        if (action == "dugme")
        {
            int button = message.GetProperty("dugme").GetInt32();
            bool down = message.GetProperty("basili").GetBoolean();
            if (button is < 0 or > 2) return;
            uint flag = button == 0 ? 2u : button == 1 ? 8u : 32u;
            if (down) { MouseEvent(flag); buttons.Add(button); }
            else if (buttons.Contains(button)) { MouseEvent(flag * 2); buttons.Remove(button); }
        }
        if (action == "tekerlek")
        {
            int delta = message.GetProperty("delta").GetInt32();
            if (delta is >= -1200 and <= 1200) MouseEvent(0x800, data: unchecked((uint)delta));
        }
    }
    public void Release()
    {
        foreach (int key in keys.ToArray()) { try { KeyEvent(key, false); } catch (InvalidOperationException) { } }
        foreach (int button in buttons.ToArray()) { try { MouseEvent(button == 0 ? 4u : button == 1 ? 16u : 64u); } catch (InvalidOperationException) { } }
        keys.Clear(); buttons.Clear();
    }
}
