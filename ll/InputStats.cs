using System;
using System.Runtime.InteropServices;
using System.Threading;
using LL;

namespace LL;

public static class InputStats
{
    private static long _mouseClickCount;
    private static long _keyboardPressCount;
    private static long _initialMouseClickCount;
    private static long _initialKeyboardPressCount;
    private static long _sessionMouseClickCount;
    private static long _sessionKeyboardPressCount;

    public static long MouseClickCount => _mouseClickCount;
    public static long KeyboardPressCount => _keyboardPressCount;
    public static long SessionMouseClickCount => _sessionMouseClickCount;
    public static long SessionKeyboardPressCount => _sessionKeyboardPressCount;

    private static IntPtr _hwnd;
    private static Thread _messageThread;

    // P/Invoke declarations
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterClassEx([In] ref WNDCLASSEX lpwcx);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // Structures
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUT
    {
        public RAWINPUTHEADER header;
        public RAWINPUTDATA data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RAWINPUTDATA
    {
        [FieldOffset(0)]
        public RAWMOUSE mouse;
        [FieldOffset(0)]
        public RAWKEYBOARD keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWMOUSE
    {
        public ushort usFlags;
        public uint ulButtons;
        public ushort usButtonFlags;
        public ushort usButtonData;
        public uint ulRawButtons;
        public int lLastX;
        public int lLastY;
        public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const uint WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    private const ushort HID_USAGE_GENERIC_MOUSE = 0x02;
    private const ushort HID_USAGE_GENERIC_KEYBOARD = 0x06;
    private const uint RIM_TYPEMOUSE = 0;
    private const uint RIM_TYPEKEYBOARD = 1;
    private const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
    private const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
    private const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
    private const IntPtr HWND_MESSAGE = (IntPtr)(-3);

    public static void Initialize()
    {
        // Load initial counts from runtime.json
        var runtimePath = Path.Combine(AppContext.BaseDirectory, "runtime.json");
        _initialMouseClickCount = ConfigManager.GetValue("MouseClickCount", 0L, runtimePath);
        _initialKeyboardPressCount = ConfigManager.GetValue("KeyboardPressCount", 0L, runtimePath);
        _mouseClickCount = _initialMouseClickCount;
        _keyboardPressCount = _initialKeyboardPressCount;
        _sessionMouseClickCount = 0;
        _sessionKeyboardPressCount = 0;

        // Start message thread for Raw Input
        _messageThread = new Thread(MessageLoop);
        _messageThread.IsBackground = true;
        _messageThread.Start();
    }

    public static void UpdateStats()
    {
        var runtimePath = Path.Combine(AppContext.BaseDirectory, "runtime.json");
        ConfigManager.SetValue("MouseClickCount", _mouseClickCount, runtimePath);
        ConfigManager.SetValue("KeyboardPressCount", _keyboardPressCount, runtimePath);
    }

    public static void Cleanup()
    {
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private static void MessageLoop()
    {
        IntPtr hInstance = GetModuleHandle(null);
        string className = "InputStatsWindow";

        // Register window class
        WNDCLASSEX wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate<WndProcDelegate>(WndProcCallback),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = className,
            hIconSm = IntPtr.Zero
        };

        if (!RegisterClassEx(ref wc))
        {
            int error = Marshal.GetLastWin32Error();
            Console.WriteLine($"Failed to register window class. Error: {error}");
            return;
        }

        // Create message-only window
        _hwnd = CreateWindowEx(0, className, null, 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            Console.WriteLine($"Failed to create window. Error: {error}");
            return;
        }

        // Register Raw Input devices
        var devices = new[]
        {
            new RAWINPUTDEVICE { usUsagePage = HID_USAGE_PAGE_GENERIC, usUsage = HID_USAGE_GENERIC_MOUSE, dwFlags = RIDEV_INPUTSINK, hwndTarget = _hwnd },
            new RAWINPUTDEVICE { usUsagePage = HID_USAGE_PAGE_GENERIC, usUsage = HID_USAGE_GENERIC_KEYBOARD, dwFlags = RIDEV_INPUTSINK, hwndTarget = _hwnd }
        };

        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
        {
            int error = Marshal.GetLastWin32Error();
            Console.WriteLine($"Failed to register Raw Input devices. Error: {error}");
            return;
        }

        // Message loop
        MSG msg;
        while (GetMessage(out msg, _hwnd, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private static void ProcessRawInput(IntPtr lParam)
    {
        uint dwSize = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref dwSize, (uint)Marshal.SizeOf<RAWINPUTHEADER>());
        if (dwSize == 0) return;

        IntPtr pData = Marshal.AllocHGlobal((int)dwSize);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, pData, ref dwSize, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) == dwSize)
            {
                RAWINPUT raw = Marshal.PtrToStructure<RAWINPUT>(pData);
                if (raw.header.dwType == RIM_TYPEMOUSE)
                {
                    Console.WriteLine($"Mouse button flags: {raw.data.mouse.usButtonFlags:X}");
                    if ((raw.data.mouse.usButtonFlags & RI_MOUSE_LEFT_BUTTON_DOWN) != 0 ||
                        (raw.data.mouse.usButtonFlags & RI_MOUSE_RIGHT_BUTTON_DOWN) != 0 ||
                        (raw.data.mouse.usButtonFlags & RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0)
                    {
                        Interlocked.Increment(ref _mouseClickCount);
                        Interlocked.Increment(ref _sessionMouseClickCount);
                    }
                }
                else if (raw.header.dwType == RIM_TYPEKEYBOARD)
                {
                    Console.WriteLine($"Keyboard flags: {raw.data.keyboard.Flags:X}");
                    if (raw.data.keyboard.Flags == 0) // Key down
                    {
                        Interlocked.Increment(ref _keyboardPressCount);
                        Interlocked.Increment(ref _sessionKeyboardPressCount);
                    }
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pData);
        }
    }

    // P/Invoke for message loop
    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    private static IntPtr WndProcCallback(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT)
        {
            // Handle Raw Input
            ProcessRawInput(lParam);
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);
}