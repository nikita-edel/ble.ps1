using System.Runtime.InteropServices;

namespace Ble;

/// <summary>
/// Caps lock state read from the OS, not the terminal (a terminal never reports it).
/// Same sources as capsdetect.nvim's lua/capsdetect/platform. null = unknown or unsupported.
/// </summary>
public static class CapsLock
{
    public static bool? Read()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return (GetKeyState(0x14) & 1) != 0;   // VK_CAPITAL toggle bit
            if (OperatingSystem.IsLinux()) return ReadLinux();
            if (OperatingSystem.IsMacOS()) return (CGEventSourceFlagsState(1) & 0x10000) != 0;   // HID system state, AlphaShift
        }
        catch { }
        return null;
    }

    [DllImport("user32.dll")]
    static extern short GetKeyState(int nVirtKey);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    static extern ulong CGEventSourceFlagsState(int stateID);

    // keyboard LEDs; missing without a local keyboard (ssh, WSL, containers). The list of LEDs is
    // looked up again every 10 s, for keyboards plugged in later.
    static string[] _leds = [];
    static long _ledsFound = -1;

    static bool? ReadLinux()
    {
        if (_ledsFound < 0 || Environment.TickCount64 - _ledsFound > 10_000)
        {
            const string dir = "/sys/class/leds";
            _leds = Directory.Exists(dir)
                ? Directory.GetDirectories(dir, "input*::capslock").Select(d => Path.Combine(d, "brightness")).ToArray()
                : [];
            _ledsFound = Environment.TickCount64;
        }
        if (_leds.Length == 0) return null;
        foreach (var led in _leds)
        {
            try { if (File.ReadAllText(led).Trim() is not ("" or "0")) return true; }
            catch { }
        }
        return false;
    }
}
