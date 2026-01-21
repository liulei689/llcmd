using System.Runtime.InteropServices;
using System.Text;
using LL;
using System.Threading;
using System.Diagnostics;
using LL.Native;

public static class HotkeyManager
{
    // Using RegisterHotKey on a dedicated message thread.
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_ALT = 0x0001;
    private const int VK_T = 0x54; // T key

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT p);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const uint WM_CHAR = 0x0102;
    private const uint WM_SETTEXT = 0x000C;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    private static Thread? _msgThread;
    private static int _hotkeyId = 1;
    private static string _presetText = "Hello World";

    public static void Initialize()
    {
        _presetText = ConfigManager.GetValue("PresetText", "Hello World");

        // Start a dedicated STA thread to register the hotkey and run a message loop
        _msgThread = new Thread(() =>
        {
            // Register hotkey associated with this thread (hWnd = NULL)
            uint mods = MOD_ALT; // Alt
            bool ok = RegisterHotKey(IntPtr.Zero, _hotkeyId, mods, (uint)VK_T);
            if (ok)
            {
                Console.WriteLine($"Hotkey registered: Alt+T (id={_hotkeyId})");
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                Console.WriteLine($"RegisterHotKey failed: {err}");
                Debug.WriteLine($"RegisterHotKey failed: {err}");
                // still run loop to allow cleanup if needed
            }

            try
            {
                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) != 0)
                {
                    if (msg.message == WM_HOTKEY)
                    {
                       // Console.WriteLine($"WM_HOTKEY received (id={msg.wParam})");
                        try { HandleHotkey(); } catch (Exception ex) { Console.WriteLine($"Hotkey handler error: {ex}"); Debug.WriteLine($"Hotkey handler error: {ex}"); }
                    }
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            finally
            {
                // Unregister on thread exit
                UnregisterHotKey(IntPtr.Zero, _hotkeyId);
            }
        });

        _msgThread.IsBackground = true;
        _msgThread.SetApartmentState(ApartmentState.STA);
        _msgThread.Start();
    }

    public static void HandleHotkey()
    {
        // Toggle minimize/restore the console window
        IntPtr hWnd = NativeMethods.GetConsoleWindow();
        if (hWnd != IntPtr.Zero)
        {
            if (IsIconic(hWnd))
            {
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
            }
            else
            {
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_MINIMIZE);
            }
        }
    }

    private static bool TryFocusAndSendInput(IntPtr hwnd, string text)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            uint targetThread = GetWindowThreadProcessId(hwnd, out _);
            uint currentThread = GetCurrentThreadId();
            // Attach input so SetFocus works across threads
            if (!AttachThreadInput(currentThread, targetThread, true))
            {
                Console.WriteLine($"AttachThreadInput failed: {Marshal.GetLastWin32Error()}");
            }

            // Bring to foreground and set focus
            SetForegroundWindow(hwnd);
            SetFocus(hwnd);
            Thread.Sleep(50); // wait for focus to apply

            // Send unicode characters via SendInput
            foreach (char c in text)
            {
                INPUT[] inputs = new INPUT[2];
                inputs[0].type = 1; // keyboard
                inputs[0].U.ki.wVk = 0;
                inputs[0].U.ki.wScan = (ushort)c;
                inputs[0].U.ki.dwFlags = KEYEVENTF_UNICODE;
                inputs[0].U.ki.time = 0;
                inputs[0].U.ki.dwExtraInfo = UIntPtr.Zero;

                inputs[1] = inputs[0];
                inputs[1].U.ki.dwFlags |= KEYEVENTF_KEYUP;

                uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
                if (sent == 0)
                {
                    Console.WriteLine($"SendInput failed for '{c}' (GetLastError={Marshal.GetLastWin32Error()})");
                }
            }

            Thread.Sleep(20);
            AttachThreadInput(currentThread, targetThread, false);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TryFocusAndSendInput failed: {ex}");
            Debug.WriteLine($"TryFocusAndSendInput failed: {ex}");
            return false;
        }
    }

    public static void Cleanup()
    {
        try
        {
            UnregisterHotKey(IntPtr.Zero, _hotkeyId);
            if (_msgThread != null && _msgThread.IsAlive)
            {
                _msgThread.Interrupt();
                _msgThread = null;
            }
        }
        catch { }
    }
}
