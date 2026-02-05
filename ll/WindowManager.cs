using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LL;

/// <summary>
/// 窗口管理器 - 窗口置顶、透明度控制、窗口列表等
/// </summary>
public static class WindowManager
{
    /// <summary>
    /// 处理窗口管理命令
    /// </summary>
    public static void Handle(string[] args)
    {
        if (args.Length == 0)
        {
            ShowUsage();
            return;
        }

        var subCommand = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();

        try
        {
            switch (subCommand)
            {
                case "top":
                case "t":
                    ToggleTopmost(subArgs);
                    break;
                case "list":
                case "l":
                    ListWindows(subArgs);
                    break;
                case "opacity":
                case "o":
                case "trans":
                    SetWindowOpacity(subArgs);
                    break;
                case "min":
                case "minimize":
                    MinimizeAllExceptCurrent();
                    break;
                case "close":
                case "c":
                    CloseWindow(subArgs);
                    break;
                case "activate":
                case "a":
                    ActivateWindow(subArgs);
                    break;
                case "info":
                case "i":
                    ShowActiveWindowInfo();
                    break;
                case "move":
                case "m":
                    MoveWindow(subArgs);
                    break;
                case "resize":
                case "r":
                    ResizeWindow(subArgs);
                    break;
                default:
                    UI.PrintError($"未知子命令: {subCommand}");
                    ShowUsage();
                    break;
            }
        }
        catch (Exception ex)
        {
            UI.PrintError($"窗口操作失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 显示用法
    /// </summary>
    private static void ShowUsage()
    {
        UI.PrintHeader("窗口管理器");
        UI.PrintItem("win top [title]", "置顶/取消置顶指定窗口（默认当前窗口）");
        UI.PrintItem("win list [filter]", "列出所有可见窗口");
        UI.PrintItem("win opacity <0-255> [title]", "设置窗口透明度（0=完全透明，255=不透明）");
        UI.PrintItem("win min", "最小化除当前窗口外的所有窗口");
        UI.PrintItem("win close <title|index>", "关闭指定窗口");
        UI.PrintItem("win activate <title|index>", "激活指定窗口");
        UI.PrintItem("win info", "显示当前活动窗口信息");
        UI.PrintItem("win move <x> <y> [title]", "移动窗口到指定位置");
        UI.PrintItem("win resize <width> <height> [title]", "调整窗口大小");
    }

    /// <summary>
    /// 切换窗口置顶状态
    /// </summary>
    private static void ToggleTopmost(string[] args)
    {
        IntPtr hWnd;
        string windowTitle;

        if (args.Length > 0)
        {
            // 通过标题查找窗口
            var title = string.Join(" ", args);
            hWnd = FindWindowByTitle(title);
            if (hWnd == IntPtr.Zero)
            {
                UI.PrintError($"未找到窗口: {title}");
                return;
            }
            windowTitle = GetWindowText(hWnd);
        }
        else
        {
            // 使用当前活动窗口
            hWnd = NativeMethodsWin.GetForegroundWindow();
            windowTitle = GetWindowText(hWnd);
        }

        // 获取当前置顶状态
        var style = (uint)NativeMethodsWin.GetWindowLong(hWnd, NativeMethodsWin.GWL_EXSTYLE);
        bool isTopmost = (style & NativeMethodsWin.WS_EX_TOPMOST) != 0;

        // 切换置顶状态
        var result = NativeMethodsWin.SetWindowPos(hWnd,
            isTopmost ? NativeMethodsWin.HWND_NOTOPMOST : NativeMethodsWin.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethodsWin.SWP_NOMOVE | NativeMethodsWin.SWP_NOSIZE | NativeMethodsWin.SWP_SHOWWINDOW);

        if (result)
        {
            UI.PrintSuccess($"已{(isTopmost ? "取消置顶" : "置顶")}窗口: {windowTitle}");
        }
        else
        {
            UI.PrintError("操作失败");
        }
    }

    /// <summary>
    /// 列出所有可见窗口
    /// </summary>
    private static void ListWindows(string[] args)
    {
        var filter = args.Length > 0 ? args[0].ToLowerInvariant() : null;
        var windows = GetVisibleWindows();

        if (filter != null)
        {
            windows = windows.Where(w => w.Title.ToLowerInvariant().Contains(filter)).ToList();
        }

        if (windows.Count == 0)
        {
            UI.PrintInfo(filter != null ? $"未找到匹配 '{filter}' 的窗口" : "未找到可见窗口");
            return;
        }

        UI.PrintHeader($"窗口列表 (共 {windows.Count} 个)");
        
        // 获取当前活动窗口
        var activeHwnd = NativeMethodsWin.GetForegroundWindow();

        for (int i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            var isActive = w.Handle == activeHwnd ? " [活动]" : "";
            var isTopmost = w.IsTopmost ? " [置顶]" : "";
            UI.PrintItem($"{i + 1,2}. {w.Title}{isActive}{isTopmost}", $"{w.ProcessName} (PID: {w.ProcessId})");
        }
    }

    /// <summary>
    /// 设置窗口透明度
    /// </summary>
    private static void SetWindowOpacity(string[] args)
    {
        if (args.Length == 0 || !byte.TryParse(args[0], out byte opacity))
        {
            UI.PrintError("请提供有效的透明度值 (0-255)");
            UI.PrintInfo("用法: win opacity <0-255> [窗口标题]");
            UI.PrintInfo("  0   = 完全透明");
            UI.PrintInfo("  128 = 半透明");
            UI.PrintInfo("  255 = 完全不透明");
            return;
        }

        IntPtr hWnd;
        string windowTitle;

        if (args.Length > 1)
        {
            var title = string.Join(" ", args.Skip(1));
            hWnd = FindWindowByTitle(title);
            if (hWnd == IntPtr.Zero)
            {
                UI.PrintError($"未找到窗口: {title}");
                return;
            }
            windowTitle = GetWindowText(hWnd);
        }
        else
        {
            hWnd = NativeMethodsWin.GetForegroundWindow();
            windowTitle = GetWindowText(hWnd);
        }

        // 设置窗口样式以支持透明度
        var style = NativeMethodsWin.GetWindowLong(hWnd, NativeMethodsWin.GWL_EXSTYLE);
        NativeMethodsWin.SetWindowLong(hWnd, NativeMethodsWin.GWL_EXSTYLE, (int)((uint)style | NativeMethodsWin.WS_EX_LAYERED));

        // 设置透明度
        var result = NativeMethodsWin.SetLayeredWindowAttributes(hWnd, 0, opacity, NativeMethodsWin.LWA_ALPHA);

        if (result)
        {
            var percent = (int)(opacity / 255.0 * 100);
            UI.PrintSuccess($"已设置窗口 '{windowTitle}' 透明度为 {percent}%");
        }
        else
        {
            UI.PrintError("设置透明度失败");
        }
    }

    /// <summary>
    /// 最小化除当前窗口外的所有窗口
    /// </summary>
    private static void MinimizeAllExceptCurrent()
    {
        var currentHwnd = NativeMethodsWin.GetForegroundWindow();
        var windows = GetVisibleWindows();
        int count = 0;

        foreach (var w in windows)
        {
            if (w.Handle != currentHwnd && !w.IsMinimized)
            {
                NativeMethodsWin.ShowWindow(w.Handle, NativeMethodsWin.SW_MINIMIZE);
                count++;
            }
        }

        UI.PrintSuccess($"已最小化 {count} 个窗口");
    }

    /// <summary>
    /// 关闭指定窗口
    /// </summary>
    private static void CloseWindow(string[] args)
    {
        if (args.Length == 0)
        {
            UI.PrintError("请提供窗口标题或序号");
            return;
        }

        var input = string.Join(" ", args);
        var hWnd = FindWindowByInput(input);

        if (hWnd == IntPtr.Zero)
        {
            UI.PrintError($"未找到窗口: {input}");
            return;
        }

        var title = GetWindowText(hWnd);
        
        // 发送关闭消息
        NativeMethodsWin.PostMessage(hWnd, NativeMethodsWin.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        UI.PrintSuccess($"已关闭窗口: {title}");
    }

    /// <summary>
    /// 激活指定窗口
    /// </summary>
    private static void ActivateWindow(string[] args)
    {
        if (args.Length == 0)
        {
            UI.PrintError("请提供窗口标题或序号");
            return;
        }

        var input = string.Join(" ", args);
        var hWnd = FindWindowByInput(input);

        if (hWnd == IntPtr.Zero)
        {
            UI.PrintError($"未找到窗口: {input}");
            return;
        }

        var title = GetWindowText(hWnd);
        
        // 恢复窗口（如果最小化）
        if (NativeMethodsWin.IsIconic(hWnd))
        {
            NativeMethodsWin.ShowWindow(hWnd, NativeMethodsWin.SW_RESTORE);
        }

        // 激活窗口
        NativeMethodsWin.SetForegroundWindow(hWnd);
        UI.PrintSuccess($"已激活窗口: {title}");
    }

    /// <summary>
    /// 显示当前活动窗口信息
    /// </summary>
    private static void ShowActiveWindowInfo()
    {
        var hWnd = NativeMethodsWin.GetForegroundWindow();
        var title = GetWindowText(hWnd);
        var rect = GetWindowRect(hWnd);
        var style = NativeMethodsWin.GetWindowLong(hWnd, NativeMethodsWin.GWL_STYLE);
        var exStyle = NativeMethodsWin.GetWindowLong(hWnd, NativeMethodsWin.GWL_EXSTYLE);

        UI.PrintHeader("当前窗口信息");
        UI.PrintResult("窗口句柄", $"0x{hWnd.ToInt64():X8}");
        UI.PrintResult("窗口标题", title);
        UI.PrintResult("位置", $"({rect.Left}, {rect.Top})");
        UI.PrintResult("大小", $"{rect.Width} x {rect.Height}");
        UI.PrintResult("置顶状态", (exStyle & NativeMethodsWin.WS_EX_TOPMOST) != 0 ? "是" : "否");
        UI.PrintResult("最大化", NativeMethodsWin.IsZoomed(hWnd) ? "是" : "否");
        UI.PrintResult("最小化", NativeMethodsWin.IsIconic(hWnd) ? "是" : "否");

        // 获取进程信息
        _ = NativeMethodsWin.GetWindowThreadProcessId(hWnd, out uint pid);
        try
        {
            var process = System.Diagnostics.Process.GetProcessById((int)pid);
            UI.PrintResult("进程名", process.ProcessName);
            UI.PrintResult("进程ID", pid.ToString());
            UI.PrintResult("程序路径", process.MainModule?.FileName ?? "未知");
        }
        catch
        {
            UI.PrintResult("进程ID", pid.ToString());
        }
    }

    /// <summary>
    /// 移动窗口
    /// </summary>
    private static void MoveWindow(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out int x) || !int.TryParse(args[1], out int y))
        {
            UI.PrintError("请提供有效的坐标");
            UI.PrintInfo("用法: win move <x> <y> [窗口标题]");
            return;
        }

        IntPtr hWnd;
        string windowTitle;

        if (args.Length > 2)
        {
            var title = string.Join(" ", args.Skip(2));
            hWnd = FindWindowByTitle(title);
            if (hWnd == IntPtr.Zero)
            {
                UI.PrintError($"未找到窗口: {title}");
                return;
            }
            windowTitle = GetWindowText(hWnd);
        }
        else
        {
            hWnd = NativeMethodsWin.GetForegroundWindow();
            windowTitle = GetWindowText(hWnd);
        }

        var rect = GetWindowRect(hWnd);
        var result = NativeMethodsWin.SetWindowPos(hWnd, IntPtr.Zero, x, y, 0, 0,
            NativeMethodsWin.SWP_NOSIZE | NativeMethodsWin.SWP_NOZORDER | NativeMethodsWin.SWP_SHOWWINDOW);

        if (result)
        {
            UI.PrintSuccess($"已移动窗口 '{windowTitle}' 到 ({x}, {y})");
        }
        else
        {
            UI.PrintError("移动窗口失败");
        }
    }

    /// <summary>
    /// 调整窗口大小
    /// </summary>
    private static void ResizeWindow(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out int width) || !int.TryParse(args[1], out int height))
        {
            UI.PrintError("请提供有效的宽高");
            UI.PrintInfo("用法: win resize <width> <height> [窗口标题]");
            return;
        }

        IntPtr hWnd;
        string windowTitle;

        if (args.Length > 2)
        {
            var title = string.Join(" ", args.Skip(2));
            hWnd = FindWindowByTitle(title);
            if (hWnd == IntPtr.Zero)
            {
                UI.PrintError($"未找到窗口: {title}");
                return;
            }
            windowTitle = GetWindowText(hWnd);
        }
        else
        {
            hWnd = NativeMethodsWin.GetForegroundWindow();
            windowTitle = GetWindowText(hWnd);
        }

        var rect = GetWindowRect(hWnd);
        var result = NativeMethodsWin.SetWindowPos(hWnd, IntPtr.Zero, 0, 0, width, height,
            NativeMethodsWin.SWP_NOMOVE | NativeMethodsWin.SWP_NOZORDER | NativeMethodsWin.SWP_SHOWWINDOW);

        if (result)
        {
            UI.PrintSuccess($"已调整窗口 '{windowTitle}' 大小为 {width} x {height}");
        }
        else
        {
            UI.PrintError("调整窗口大小失败");
        }
    }

    #region 辅助方法

    /// <summary>
    /// 通过输入查找窗口（支持序号或标题）
    /// </summary>
    private static IntPtr FindWindowByInput(string input)
    {
        // 尝试解析为序号
        if (int.TryParse(input, out int index))
        {
            var windows = GetVisibleWindows();
            if (index > 0 && index <= windows.Count)
            {
                return windows[index - 1].Handle;
            }
        }

        // 按标题查找
        return FindWindowByTitle(input);
    }

    /// <summary>
    /// 通过标题查找窗口
    /// </summary>
    private static IntPtr FindWindowByTitle(string title)
    {
        var windows = GetVisibleWindows();
        
        // 精确匹配
        var exact = windows.FirstOrDefault(w => 
            w.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
        if (exact.Handle != IntPtr.Zero) return exact.Handle;

        // 包含匹配
        var contains = windows.FirstOrDefault(w => 
            w.Title.ToLowerInvariant().Contains(title.ToLowerInvariant()));
        return contains.Handle;
    }

    /// <summary>
    /// 获取窗口文本
    /// </summary>
    private static string GetWindowText(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        NativeMethodsWin.GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// 获取窗口矩形
    /// </summary>
    private static (int Left, int Top, int Width, int Height) GetWindowRect(IntPtr hWnd)
    {
        NativeMethodsWin.GetWindowRect(hWnd, out NativeMethodsWin.RECT rect);
        return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>
    /// 获取所有可见窗口
    /// </summary>
    private static List<WindowInfo> GetVisibleWindows()
    {
        var windows = new List<WindowInfo>();
        
        NativeMethodsWin.EnumWindows((hWnd, lParam) =>
        {
            // 检查窗口是否可见
            if (!NativeMethodsWin.IsWindowVisible(hWnd))
                return true;

            // 获取窗口标题
            var title = GetWindowText(hWnd);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            // 获取进程信息
            uint pid = 0;
            NativeMethodsWin.GetWindowThreadProcessId(hWnd, out pid);
            
            string processName = "Unknown";
            try
            {
                processName = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            }
            catch { }

            // 检查置顶状态
            var exStyle = NativeMethodsWin.GetWindowLong(hWnd, NativeMethodsWin.GWL_EXSTYLE);
            bool isTopmost = (exStyle & NativeMethodsWin.WS_EX_TOPMOST) != 0;

            windows.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = title,
                ProcessId = (int)pid,
                ProcessName = processName,
                IsTopmost = isTopmost,
                IsMinimized = NativeMethodsWin.IsIconic(hWnd)
            });

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private struct WindowInfo
    {
        public IntPtr Handle;
        public string Title;
        public int ProcessId;
        public string ProcessName;
        public bool IsTopmost;
        public bool IsMinimized;
    }

    #endregion
}

/// <summary>
/// 窗口管理相关的 NativeMethods
/// </summary>
internal static partial class NativeMethodsWin
{
    // 常量定义
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint LWA_ALPHA = 0x00000002;
    public const uint WM_CLOSE = 0x0010;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;

    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    // 结构定义
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // API 导入
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey,
        byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
}
