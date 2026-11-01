namespace Ble;

/// <summary>Abstraction over the console so the editor can be driven by tests.</summary>
public interface ITerminal
{
    ConsoleKeyInfo ReadKey();
    bool KeyAvailable { get; }
    void Write(string s);
    int Width { get; }
    int Height { get; }
    int CursorLeft { get; }
    int CursorTop { get; }
    bool TreatControlCAsInput { get; set; }
    /// <summary>Milliseconds clock used for idle processing.</summary>
    long Now { get; }
    /// <summary>Sleeps briefly while waiting for input.</summary>
    void Wait(int ms);
}

public sealed class ConsoleTerminal : ITerminal
{
    public ConsoleKeyInfo ReadKey() => Console.ReadKey(true);
    public bool KeyAvailable { get { bool a = Console.KeyAvailable; if (a) _signaled = 0; return a; } }

    public void Write(string s)
    {
        var o = Console.Out;
        o.Write(s);
        o.Flush();
    }

    public int Width
    {
        get
        {
            try
            {
                int w = OperatingSystem.IsWindows() ? Console.BufferWidth : Console.WindowWidth;
                return w > 0 ? w : 80;
            }
            catch { return 80; }
        }
    }

    public int Height
    {
        get { try { int h = Console.WindowHeight; return h > 0 ? h : 24; } catch { return 24; } }
    }

    public int CursorLeft { get { try { return Console.CursorLeft; } catch { return 0; } } }
    public int CursorTop { get { try { return Console.CursorTop; } catch { return 0; } } }

    public bool TreatControlCAsInput
    {
        get { try { return Console.TreatControlCAsInput; } catch { return false; } }
        set { try { Console.TreatControlCAsInput = value; } catch { } }
    }

    public long Now => Environment.TickCount64;

    // Thread.Sleep(10) sleeps a whole timer tick (~15 ms) on Windows and the full 10 ms elsewhere, so a
    // key pressed while idle waited that long. Waiting on the console input returns as soon as input arrives.
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern IntPtr GetStdHandle(int nStdHandle);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    IntPtr _stdin;
    int _signaled;

    public void Wait(int ms)
    {
        if (OperatingSystem.IsWindows() && !Console.IsInputRedirected)
        {
            if (_stdin == IntPtr.Zero) _stdin = GetStdHandle(-10);   // STD_INPUT_HANDLE
            // the handle also signals non-key events (focus, mouse, key-up), which Console.KeyAvailable
            // removes; if it keeps signaling without a key, fall back to sleeping so this never spins
            if (_stdin != IntPtr.Zero && _stdin != new IntPtr(-1) && _signaled < 8)
            {
                _signaled = WaitForSingleObject(_stdin, (uint)ms) == 0 ? _signaled + 1 : 0;
                return;
            }
            _signaled = 0;
        }
        else if (!OperatingSystem.IsWindows() && _poll && !Console.IsInputRedirected)
        {
            try
            {
                var fd = new PollFd { Fd = 0, Events = PollIn };
                // on an error or a closed terminal poll returns at once, so sleep to not spin
                if (poll(ref fd, 1, ms) >= 0 && (fd.Revents & (PollErr | PollHup | PollNval)) == 0) return;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { _poll = false; }
        }
        Thread.Sleep(ms);
    }

    const short PollIn = 1, PollErr = 8, PollHup = 16, PollNval = 32;
    bool _poll = true;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct PollFd { public int Fd; public short Events; public short Revents; }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    static extern int poll(ref PollFd fds, ulong count, int timeoutMs);

    // "libc" alone finds libc.so, which only exists with the C headers installed (and is a text file then)
    static ConsoleTerminal() => System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(ConsoleTerminal).Assembly,
        (name, _, _) => name == "libc" && OperatingSystem.IsLinux()
            && System.Runtime.InteropServices.NativeLibrary.TryLoad("libc.so.6", out var libc) ? libc : IntPtr.Zero);
}
