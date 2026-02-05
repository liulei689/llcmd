using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LL;

/// <summary>
/// 窗口管理器 - 选中即操作模式
/// </summary>
public static class WindowManager
{
    private static IntPtr _pickedWindow = IntPtr.Zero;
    private static string _pickedTitle = "";
    private static DateTime _pickedTime;
    private static readonly TimeSpan _pickTimeout = TimeSpan.FromMinutes(30);
    private static bool _keepPicked = false;  // 永久锁定模式
    
    private static readonly HttpClient _httpClient = new();
    private static readonly string _snapshotsFile;
    private static List<WindowSnapshot> _snapshots = new();
    
    static WindowManager()
    {
        _snapshotsFile = Path.Combine(AppContext.BaseDirectory, "window_snapshots.json");
        LoadSnapshots();
    }

    /// <summary>
    /// 处理窗口管理命令 - 选中即操作
    /// </summary>
    public static void Handle(string[] args)
    {
        if (args.Length == 0)
        {
            ShowUsage();
            return;
        }

        var cmd = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();

        try
        {
            switch (cmd)
            {
                // ===== 选择窗口 =====
                case "pick" or "p":
                    PickWindow(); break;
                case "this" or ".":
                    PickCurrentWindow(); break;
                case "last":
                    UseLastPicked(); break;
                case "keep" or "k":
                    KeepPickedWindow(); break;
                case "unkeep" or "uk":
                    UnkeepPickedWindow(); break;
                    
                // ===== 选中窗口的操作（无需再指定窗口） =====
                case "left" or "l":
                    LayoutPicked("left"); break;
                case "right" or "r":
                    LayoutPicked("right"); break;
                case "top" or "t":
                    LayoutPicked("top"); break;
                case "bottom" or "b":
                    LayoutPicked("bottom"); break;
                case "max" or "x":
                    MaximizePicked(); break;
                case "min" or "n":
                    MinimizePicked(); break;
                case "restore" or "rs":
                    RestorePicked(); break;
                case "center" or "c":
                    CenterPicked(); break;
                case "full" or "f":
                    FullscreenPicked(); break;
                    
                // ===== 属性操作 =====
                case "topmost" or "tm":
                    ToggleTopmostPicked(); break;
                case "opacity" or "o":
                    SetOpacityPicked(subArgs); break;
                case "fade":
                    FadePicked(subArgs); break;
                case "flash":
                    FlashPicked(); break;
                case "shake":
                    ShakePicked(); break;
                    
                // ===== 关闭/隐藏 =====
                case "close" or "cl":
                    ClosePicked(); break;
                case "hide" or "h":
                    HidePicked(); break;
                case "kill" or "k":
                    KillPicked(); break;
                    
                // ===== 信息 =====
                case "info" or "i":
                    ShowPickedInfo(); break;
                case "list" or "ls":
                    ListWindows(subArgs); break;
                    
                // ===== 批量操作 =====
                case "grid":
                    ArrangeGrid(subArgs); break;
                case "cascade":
                    ArrangeCascade(); break;
                case "tile":
                    TileWindows(); break;
                case "minothers":
                    MinimizeOthers(); break;
                case "boss":
                    BossKey(); break;
                    
                // ===== 快照 =====
                case "save" or "s":
                    SaveSnapshot(subArgs); break;
                case "load":
                    LoadSnapshot(subArgs); break;
                case "snapshots":
                    ListSnapshots(); break;
                case "del":
                    DeleteSnapshot(subArgs); break;
                    
                // ===== 系统 =====
                case "dark" or "d":
                    ToggleDarkMode(); break;
                case "refresh":
                    RefreshDesktop(); break;
                    
                // ===== 新增酷炫功能 =====
                case "shot":
                    CaptureWindow(subArgs); break;
                case "clickthrough" or "ct":
                    ToggleClickThrough(); break;
                case "magnify" or "mag":
                    ShowMagnifier(); break;
                case "clone":
                    CloneWindow(); break;
                case "pin":
                    TogglePinWindow(); break;
                case "blur":
                    ToggleBlurWindow(); break;
                    
                default:
                    UI.PrintError($"未知命令: {cmd}");
                    ShowUsage();
                    break;
            }
        }
        catch (Exception ex)
        {
            UI.PrintError($"操作失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 显示用法
    /// </summary>
    private static void ShowUsage()
    {
        UI.PrintHeader("窗口管理器 - 选中即操作");
        Console.WriteLine();
        UI.PrintItem("核心流程: pick → left/right/max/close ...", "");
        Console.WriteLine();
        
        Console.WriteLine("【选择窗口】");
        UI.PrintItem("pick/p", "鼠标十字线选择窗口");
        UI.PrintItem("this/.", "选择当前活动窗口");
        UI.PrintItem("last", "使用上次选中的窗口");
        UI.PrintItem("keep/k", "永久锁定选中(不过期)");
        UI.PrintItem("unkeep/uk", "取消锁定");
        Console.WriteLine();
        
        Console.WriteLine("【布局操作】");
        UI.PrintItem("left/l", "选中窗口左半屏");
        UI.PrintItem("right/r", "选中窗口右半屏");
        UI.PrintItem("top/t", "选中窗口上半屏");
        UI.PrintItem("bottom/b", "选中窗口下半屏");
        UI.PrintItem("max/x", "最大化");
        UI.PrintItem("min/n", "最小化");
        UI.PrintItem("restore/rs", "恢复");
        UI.PrintItem("center/c", "居中");
        UI.PrintItem("full/f", "全屏(无边框)");
        Console.WriteLine();
        
        Console.WriteLine("【属性效果】");
        UI.PrintItem("topmost/tm", "置顶/取消置顶");
        UI.PrintItem("opacity/o <0-255>", "透明度");
        UI.PrintItem("fade <目标>", "渐变动画");
        UI.PrintItem("flash", "闪烁提醒");
        UI.PrintItem("shake", "抖动效果");
        Console.WriteLine();
        
        Console.WriteLine("【关闭隐藏】");
        UI.PrintItem("close/cl", "关闭选中窗口");
        UI.PrintItem("hide/h", "隐藏窗口");
        UI.PrintItem("kill/k", "强制结束进程");
        Console.WriteLine();
        
        Console.WriteLine("【批量操作】");
        UI.PrintItem("grid [n]", "网格排列所有窗口");
        UI.PrintItem("cascade", "层叠排列");
        UI.PrintItem("tile", "平铺排列");
        UI.PrintItem("minothers", "最小化其他窗口");
        UI.PrintItem("boss", "老板键(最小化全部)");
        Console.WriteLine();
        
        Console.WriteLine("【快照】");
        UI.PrintItem("save/s [name]", "保存布局快照");
        UI.PrintItem("load <name>", "恢复快照");
        UI.PrintItem("snapshots", "列出快照");
        Console.WriteLine();
        
        Console.WriteLine("【示例】");
        UI.PrintItem("win pick + win left", "选择窗口并左半屏");
        UI.PrintItem("win this + win max", "当前窗口最大化");
        UI.PrintItem("win pick + win o 150", "选择窗口设透明度");
        Console.WriteLine();
        
        Console.WriteLine("【新增功能】");
        UI.PrintItem("shot [文件名]", "窗口截图保存");
        UI.PrintItem("clickthrough/ct", "点击穿透模式(透明+穿透)");
        UI.PrintItem("magnify/mag", "放大镜跟随鼠标");
        UI.PrintItem("clone", "克隆窗口(再开同款应用)");
        UI.PrintItem("pin", "钉住窗口(置顶贴图模式)");
        UI.PrintItem("blur", "窗口背景模糊(亚克力效果)");
    }

    #region 窗口选择

    /// <summary>
    /// 鼠标十字线选择窗口
    /// </summary>
    private static void PickWindow()
    {
        UI.PrintInfo("3秒后将用鼠标位置选择窗口...");
        UI.PrintInfo("请移动鼠标到目标窗口上...");
        
        for (int i = 3; i > 0; i--)
        {
            Console.Write($"\r{i}... ");
            Thread.Sleep(1000);
        }
        Console.WriteLine("\rgo!   ");

        var point = new POINT();
        GetCursorPos(out point);
        
        // 从鼠标位置获取窗口
        var hWnd = WindowFromPoint(point);
        
        // 获取根窗口（避免选到子控件）
        var rootWnd = GetAncestor(hWnd, GA_ROOT);
        if (rootWnd != IntPtr.Zero) hWnd = rootWnd;
        
        if (hWnd == IntPtr.Zero || !IsWindowVisible(hWnd))
        {
            UI.PrintError("未找到有效窗口");
            return;
        }

        SelectWindow(hWnd);
        
        // 高亮显示选中
        FlashWindow(hWnd, 3);
        UI.PrintSuccess($"已选中: {_pickedTitle}");
        UI.PrintInfo("现在可以直接使用 left/right/max/close 等命令操作此窗口");
    }

    /// <summary>
    /// 选择当前活动窗口
    /// </summary>
    private static void PickCurrentWindow()
    {
        var hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero)
        {
            UI.PrintError("没有活动窗口");
            return;
        }
        
        SelectWindow(hWnd);
        UI.PrintSuccess($"已选中当前窗口: {_pickedTitle}");
    }

    /// <summary>
    /// 使用上次选中的窗口
    /// </summary>
    private static void UseLastPicked()
    {
        if (_pickedWindow == IntPtr.Zero || !IsWindow(_pickedWindow))
        {
            UI.PrintError("没有缓存的窗口，请先使用 pick 或 this");
            return;
        }
        
        if (DateTime.Now - _pickedTime > _pickTimeout)
        {
            UI.PrintInfo("选中已超时，请重新选择");
            _pickedWindow = IntPtr.Zero;
            return;
        }
        
        // 确保窗口仍然存在
        var title = GetWindowTextSafe(_pickedWindow);
        if (string.IsNullOrEmpty(title))
        {
            UI.PrintError("缓存的窗口已关闭");
            _pickedWindow = IntPtr.Zero;
            return;
        }
        
        _pickedTitle = title;
        UI.PrintSuccess($"继续使用: {_pickedTitle}");
        
        // 激活窗口
        SetForegroundWindow(_pickedWindow);
        FlashWindow(_pickedWindow, 2);
    }

    private static void SelectWindow(IntPtr hWnd)
    {
        _pickedWindow = hWnd;
        _pickedTitle = GetWindowTextSafe(hWnd);
        _pickedTime = DateTime.Now;
    }

    private static IntPtr GetPickedWindow()
    {
        if (_pickedWindow == IntPtr.Zero || !IsWindow(_pickedWindow))
        {
            // 如果没有选中的，使用当前活动窗口
            var current = GetForegroundWindow();
            if (current != IntPtr.Zero)
            {
                SelectWindow(current);
                return current;
            }
            throw new InvalidOperationException("没有选中的窗口，请先使用 win pick 或 win this");
        }
        
        // 如果是锁定模式，不检查超时
        if (!_keepPicked && DateTime.Now - _pickedTime > _pickTimeout)
        {
            _pickedWindow = IntPtr.Zero;
            throw new InvalidOperationException("选中已超时(30分钟)，请重新选择或使用 win keep 锁定");
        }
        
        return _pickedWindow;
    }

    /// <summary>
    /// 永久锁定选中的窗口
    /// </summary>
    private static void KeepPickedWindow()
    {
        if (_pickedWindow == IntPtr.Zero || !IsWindow(_pickedWindow))
        {
            // 没有选中就自动选当前窗口
            var current = GetForegroundWindow();
            if (current == IntPtr.Zero)
            {
                UI.PrintError("没有可锁定的窗口");
                return;
            }
            SelectWindow(current);
        }
        
        _keepPicked = true;
        UI.PrintSuccess($"已锁定: {_pickedTitle}");
        UI.PrintInfo("提示：此窗口选中状态将永久有效，直到执行 win unkeep 或窗口关闭");
    }

    /// <summary>
    /// 取消锁定
    /// </summary>
    private static void UnkeepPickedWindow()
    {
        _keepPicked = false;
        if (_pickedWindow != IntPtr.Zero)
        {
            UI.PrintSuccess($"已取消锁定: {_pickedTitle}");
            UI.PrintInfo("提示：恢复30分钟超时机制");
        }
        else
        {
            UI.PrintInfo("当前没有锁定的窗口");
        }
    }

    private static void FlashWindow(IntPtr hWnd, int times)
    {
        Task.Run(() =>
        {
            for (int i = 0; i < times; i++)
            {
                FlashWindow(hWnd, true);
                Thread.Sleep(200);
            }
        });
    }

    #endregion

    #region 布局操作

    private static void LayoutPicked(string position)
    {
        var hWnd = GetPickedWindow();
        var bounds = GetWindowScreenBounds(hWnd);
        int x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height;

        switch (position)
        {
            case "left": w /= 2; break;
            case "right": x += w / 2; w /= 2; break;
            case "top": h /= 2; break;
            case "bottom": y += h / 2; h /= 2; break;
        }

        // 还原窗口（如果最大化/最小化）
        ShowWindow(hWnd, SW_RESTORE);
        
        SetWindowPos(hWnd, IntPtr.Zero, x, y, w, h, SWP_NOZORDER | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
        UI.PrintSuccess($"{_pickedTitle} → {position}");
    }

    private static void MaximizePicked()
    {
        var hWnd = GetPickedWindow();
        ShowWindow(hWnd, SW_MAXIMIZE);
        UI.PrintSuccess($"{_pickedTitle} → 最大化");
    }

    private static void MinimizePicked()
    {
        var hWnd = GetPickedWindow();
        ShowWindow(hWnd, SW_MINIMIZE);
        UI.PrintSuccess($"{_pickedTitle} → 最小化");
    }

    private static void RestorePicked()
    {
        var hWnd = GetPickedWindow();
        ShowWindow(hWnd, SW_RESTORE);
        UI.PrintSuccess($"{_pickedTitle} → 恢复");
    }

    private static void CenterPicked()
    {
        var hWnd = GetPickedWindow();
        var rect = GetWindowRect(hWnd);
        var screen = GetWindowScreenBounds(hWnd);

        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        int x = screen.X + (screen.Width - w) / 2;
        int y = screen.Y + (screen.Height - h) / 2;

        ShowWindow(hWnd, SW_RESTORE);
        SetWindowPos(hWnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_SHOWWINDOW);
        UI.PrintSuccess($"{_pickedTitle} → 居中");
    }

    private static void FullscreenPicked()
    {
        var hWnd = GetPickedWindow();
        var screen = GetWindowScreenBounds(hWnd);
        
        ShowWindow(hWnd, SW_RESTORE);
        // 移除边框样式
        var style = GetWindowLong(hWnd, GWL_STYLE);
        SetWindowLong(hWnd, GWL_STYLE, (int)(style & ~WS_CAPTION & ~WS_THICKFRAME));
        
        SetWindowPos(hWnd, HWND_TOPMOST, screen.X, screen.Y, screen.Width, screen.Height, 
            SWP_SHOWWINDOW | SWP_FRAMECHANGED);
        UI.PrintSuccess($"{_pickedTitle} → 全屏");
    }

    #endregion

    #region 属性效果

    private static void ToggleTopmostPicked()
    {
        var hWnd = GetPickedWindow();
        var exStyle = (uint)GetWindowLong(hWnd, GWL_EXSTYLE);
        bool isTopmost = (exStyle & WS_EX_TOPMOST) != 0;

        SetWindowPos(hWnd, isTopmost ? HWND_NOTOPMOST : HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

        UI.PrintSuccess($"{_pickedTitle} → {(isTopmost ? "取消置顶" : "置顶")}");
    }

    private static void SetOpacityPicked(string[] args)
    {
        if (args.Length == 0 || !byte.TryParse(args[0], out byte opacity))
        {
            UI.PrintError("用法: win o <0-255>");
            return;
        }

        var hWnd = GetPickedWindow();
        var style = GetWindowLong(hWnd, GWL_EXSTYLE);
        SetWindowLong(hWnd, GWL_EXSTYLE, (int)(style | WS_EX_LAYERED));
        SetLayeredWindowAttributes(hWnd, 0, opacity, LWA_ALPHA);

        UI.PrintSuccess($"{_pickedTitle} → 透明度 {(int)(opacity / 255.0 * 100)}%");
    }

    private static void FadePicked(string[] args)
    {
        if (args.Length == 0 || !byte.TryParse(args[0], out byte target))
        {
            UI.PrintError("用法: win fade <0-255>");
            return;
        }

        var hWnd = GetPickedWindow();
        var title = _pickedTitle;
        
        var style = GetWindowLong(hWnd, GWL_EXSTYLE);
        SetWindowLong(hWnd, GWL_EXSTYLE, (int)(style | WS_EX_LAYERED));

        Task.Run(() =>
        {
            for (byte i = 255; i != target; i = (byte)(i > target ? i - 5 : i + 5))
            {
                SetLayeredWindowAttributes(hWnd, 0, i, LWA_ALPHA);
                Thread.Sleep(20);
            }
            SetLayeredWindowAttributes(hWnd, 0, target, LWA_ALPHA);
        });

        UI.PrintSuccess($"{title} → 渐变到 {(int)(target / 255.0 * 100)}%");
    }

    private static void FlashPicked()
    {
        var hWnd = GetPickedWindow();
        FlashWindow(hWnd, 5);
        UI.PrintSuccess($"{_pickedTitle} → 闪烁");
    }

    private static void ShakePicked()
    {
        var hWnd = GetPickedWindow();
        var rect = GetWindowRect(hWnd);
        int x = rect.Left, y = rect.Top;

        Task.Run(() =>
        {
            for (int i = 0; i < 10; i++)
            {
                SetWindowPos(hWnd, IntPtr.Zero, x + (i % 2 == 0 ? 10 : -10), y, 0, 0,
                    SWP_NOSIZE | SWP_NOZORDER);
                Thread.Sleep(50);
            }
            SetWindowPos(hWnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
        });

        UI.PrintSuccess($"{_pickedTitle} → 抖动");
    }

    #endregion

    #region 关闭隐藏

    private static void ClosePicked()
    {
        var hWnd = GetPickedWindow();
        var title = _pickedTitle;
        PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        _pickedWindow = IntPtr.Zero;
        UI.PrintSuccess($"已关闭: {title}");
    }

    private static void HidePicked()
    {
        var hWnd = GetPickedWindow();
        ShowWindow(hWnd, SW_HIDE);
        UI.PrintSuccess($"{_pickedTitle} → 隐藏");
    }

    private static void KillPicked()
    {
        var hWnd = GetPickedWindow();
        GetWindowThreadProcessId(hWnd, out uint pid);
        
        try
        {
            var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            var name = proc.ProcessName;
            proc.Kill();
            _pickedWindow = IntPtr.Zero;
            UI.PrintSuccess($"已结束进程: {name} (PID:{pid})");
        }
        catch (Exception ex)
        {
            UI.PrintError($"结束进程失败: {ex.Message}");
        }
    }

    #endregion

    #region 信息显示

    private static void ShowPickedInfo()
    {
        var hWnd = GetPickedWindow();
        var rect = GetWindowRect(hWnd);
        var style = GetWindowLong(hWnd, GWL_STYLE);
        var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

        UI.PrintHeader("选中窗口信息");
        UI.PrintResult("标题", _pickedTitle);
        UI.PrintResult("句柄", $"0x{hWnd.ToInt64():X8}");
        UI.PrintResult("位置", $"({rect.Left}, {rect.Top})");
        UI.PrintResult("大小", $"{rect.Right - rect.Left} x {rect.Bottom - rect.Top}");
        UI.PrintResult("置顶", (exStyle & WS_EX_TOPMOST) != 0 ? "是" : "否");
        UI.PrintResult("最大化", IsZoomed(hWnd) ? "是" : "否");
        UI.PrintResult("最小化", IsIconic(hWnd) ? "是" : "否");

        GetWindowThreadProcessId(hWnd, out uint pid);
        try
        {
            var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            UI.PrintResult("进程", $"{proc.ProcessName} (PID:{pid})");
        }
        catch { UI.PrintResult("PID", pid.ToString()); }
    }

    private static void ListWindows(string[] args)
    {
        var filter = args.Length > 0 ? args[0].ToLowerInvariant() : null;
        var windows = GetVisibleWindows();

        if (filter != null)
            windows = windows.Where(w => w.Title.ToLowerInvariant().Contains(filter)).ToList();

        UI.PrintHeader($"窗口列表 (共 {windows.Count} 个)");
        var active = GetForegroundWindow();

        for (int i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            var marker = w.Handle == active ? "▶ " : "  ";
            var pickMarker = w.Handle == _pickedWindow ? "👆" : "  ";
            var status = w.IsTopmost ? "📌" : (w.IsMinimized ? "🗕" : "  ");
            UI.PrintItem($"{marker}{i + 1,2}.{pickMarker}{status} {w.Title}", $"{w.ProcessName}");
        }
        
        if (_pickedWindow != IntPtr.Zero)
        {
            Console.WriteLine();
            UI.PrintInfo($"当前选中: {_pickedTitle}");
        }
    }

    #endregion

    #region 批量操作

    private static void ArrangeGrid(string[] args)
    {
        int cols = args.Length > 0 && int.TryParse(args[0], out int c) ? c : 2;
        var windows = GetVisibleWindows().Where(w => !w.IsMinimized).Take(9).ToList();
        if (windows.Count == 0) return;

        var screen = GetPrimaryScreenBounds();
        int rows = (int)Math.Ceiling((double)windows.Count / cols);
        int cellW = screen.Width / cols;
        int cellH = screen.Height / rows;

        for (int i = 0; i < windows.Count; i++)
        {
            int row = i / cols;
            int col = i % cols;
            SetWindowPos(windows[i].Handle, IntPtr.Zero,
                col * cellW, row * cellH, cellW, cellH,
                SWP_NOZORDER | SWP_SHOWWINDOW);
        }

        UI.PrintSuccess($"网格排列 {windows.Count} 个窗口 ({cols}x{rows})");
    }

    private static void ArrangeCascade()
    {
        var windows = GetVisibleWindows().Where(w => !w.IsMinimized).Take(8).ToList();
        if (windows.Count == 0) return;

        int offset = 40;
        for (int i = 0; i < windows.Count; i++)
        {
            SetWindowPos(windows[i].Handle, IntPtr.Zero,
                i * offset, i * offset, 1000, 700,
                SWP_NOZORDER | SWP_SHOWWINDOW);
        }

        UI.PrintSuccess($"层叠排列 {windows.Count} 个窗口");
    }

    private static void TileWindows()
    {
        var windows = GetVisibleWindows().Where(w => !w.IsMinimized).ToList();
        if (windows.Count < 2) return;

        var screen = GetPrimaryScreenBounds();
        int cols = (int)Math.Ceiling(Math.Sqrt(windows.Count));
        int rows = (int)Math.Ceiling((double)windows.Count / cols);
        int w = screen.Width / cols;
        int h = screen.Height / rows;

        for (int i = 0; i < windows.Count; i++)
        {
            SetWindowPos(windows[i].Handle, IntPtr.Zero,
                (i % cols) * w, (i / cols) * h, w, h,
                SWP_NOZORDER | SWP_SHOWWINDOW);
        }
        
        UI.PrintSuccess($"平铺 {windows.Count} 个窗口");
    }

    private static void MinimizeOthers()
    {
        var picked = GetPickedWindow();
        var windows = GetVisibleWindows().Where(w => w.Handle != picked && !w.IsMinimized).ToList();
        
        foreach (var w in windows)
            ShowWindow(w.Handle, SW_MINIMIZE);
            
        UI.PrintSuccess($"已最小化其他 {windows.Count} 个窗口");
    }

    private static void BossKey()
    {
        var windows = GetVisibleWindows().Where(w => !w.IsMinimized).ToList();
        foreach (var w in windows)
            ShowWindow(w.Handle, SW_MINIMIZE);
        UI.PrintSuccess($"老板键: 最小化 {windows.Count} 个窗口");
    }

    #endregion

    #region 快照

    private static void SaveSnapshot(string[] args)
    {
        var name = args.Length > 0 ? string.Join(" ", args) : $"snapshot_{DateTime.Now:MMdd_HHmmss}";
        var windows = GetVisibleWindows().Where(w => !w.IsMinimized).ToList();

        var snapshot = new WindowSnapshot
        {
            Name = name,
            CreatedAt = DateTime.Now,
            Windows = windows.Select(w =>
            {
                var rect = GetWindowRect(w.Handle);
                return new WindowState
                {
                    Title = w.Title,
                    ProcessName = w.ProcessName,
                    X = rect.Left,
                    Y = rect.Top,
                    Width = rect.Right - rect.Left,
                    Height = rect.Bottom - rect.Top,
                    IsTopmost = w.IsTopmost
                };
            }).ToList()
        };

        _snapshots.RemoveAll(s => s.Name == name);
        _snapshots.Add(snapshot);
        SaveSnapshots();
        UI.PrintSuccess($"保存快照 '{name}' ({snapshot.Windows.Count} 个窗口)");
    }

    private static void LoadSnapshot(string[] args)
    {
        if (args.Length == 0)
        {
            UI.PrintError("用法: win load <快照名>");
            return;
        }

        var name = string.Join(" ", args);
        var snapshot = _snapshots.FirstOrDefault(s => s.Name == name);
        if (snapshot == null)
        {
            UI.PrintError($"未找到快照: {name}");
            return;
        }

        var windows = GetVisibleWindows();
        int restored = 0;

        foreach (var state in snapshot.Windows)
        {
            var match = windows.FirstOrDefault(w => 
                w.Title == state.Title || w.ProcessName == state.ProcessName);

            if (match.Handle != IntPtr.Zero)
            {
                SetWindowPos(match.Handle,
                    state.IsTopmost ? HWND_TOPMOST : HWND_NOTOPMOST,
                    state.X, state.Y, state.Width, state.Height, SWP_SHOWWINDOW);
                restored++;
            }
        }

        UI.PrintSuccess($"恢复快照 '{name}' ({restored}/{snapshot.Windows.Count})");
    }

    private static void ListSnapshots()
    {
        if (_snapshots.Count == 0)
        {
            UI.PrintInfo("暂无快照");
            return;
        }

        UI.PrintHeader($"快照列表 (共 {_snapshots.Count} 个)");
        foreach (var s in _snapshots.OrderByDescending(s => s.CreatedAt))
        {
            UI.PrintItem($"• {s.Name}", $"{s.Windows.Count}窗口 {s.CreatedAt:MM-dd HH:mm}");
        }
    }

    private static void DeleteSnapshot(string[] args)
    {
        if (args.Length == 0) { UI.PrintError("用法: win del <快照名>"); return; }
        
        var name = string.Join(" ", args);
        if (_snapshots.RemoveAll(s => s.Name == name) > 0)
        {
            SaveSnapshots();
            UI.PrintSuccess($"删除快照: {name}");
        }
        else
        {
            UI.PrintError($"未找到快照: {name}");
        }
    }

    private static void LoadSnapshots()
    {
        if (File.Exists(_snapshotsFile))
        {
            try
            {
                var json = File.ReadAllText(_snapshotsFile);
                var options = new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
                _snapshots = JsonSerializer.Deserialize<List<WindowSnapshot>>(json, options) ?? new();
            }
            catch { _snapshots = new(); }
        }
    }

    private static void SaveSnapshots()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
        File.WriteAllText(_snapshotsFile, JsonSerializer.Serialize(_snapshots, options));
    }

    #endregion

    #region 系统功能

    private static void ToggleDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", true);
            if (key != null)
            {
                var current = key.GetValue("AppsUseLightTheme");
                bool isLight = current != null && (int)current == 1;
                key.SetValue("AppsUseLightTheme", isLight ? 0 : 1);
                key.SetValue("SystemUsesLightTheme", isLight ? 0 : 1);
                UI.PrintSuccess(isLight ? "已切换到深色模式" : "已切换到浅色模式");
            }
        }
        catch (Exception ex)
        {
            UI.PrintError($"切换失败: {ex.Message}");
        }
    }

    private static void RefreshDesktop()
    {
        // 发送 F5 到桌面
        var hWnd = FindWindow("Progman", "Program Manager");
        if (hWnd != IntPtr.Zero)
        {
            PostMessage(hWnd, 0x0112, (IntPtr)(IntPtr)0xF140, IntPtr.Zero); // WM_SYSCOMMAND SC_MINIMIZE
        }
        UI.PrintSuccess("桌面已刷新");
    }

    #endregion

    #region 辅助方法

    private static string GetWindowTextSafe(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static RECT GetWindowRect(IntPtr hWnd)
    {
        GetWindowRect(hWnd, out RECT rect);
        return rect;
    }

    private static List<WindowInfo> GetVisibleWindows()
    {
        var windows = new List<WindowInfo>();
        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var title = GetWindowTextSafe(hWnd);
            if (string.IsNullOrWhiteSpace(title)) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            string proc = "Unknown";
            try { proc = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }

            var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            windows.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = title,
                ProcessId = (int)pid,
                ProcessName = proc,
                IsTopmost = (exStyle & WS_EX_TOPMOST) != 0,
                IsMinimized = IsIconic(hWnd)
            });
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static (int X, int Y, int Width, int Height) GetPrimaryScreenBounds()
    {
        return (0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
    }

    private static (int X, int Y, int Width, int Height) GetWindowScreenBounds(IntPtr hWnd)
    {
        MONITORINFO mi = new() { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
        IntPtr hMonitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
        if (GetMonitorInfo(hMonitor, ref mi))
        {
            return (mi.rcWork.Left, mi.rcWork.Top, 
                mi.rcWork.Right - mi.rcWork.Left, 
                mi.rcWork.Bottom - mi.rcWork.Top);
        }
        return GetPrimaryScreenBounds();
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

    private class WindowSnapshot
    {
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public List<WindowState> Windows { get; set; } = new();
    }

    private class WindowState
    {
        public string Title { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsTopmost { get; set; }
    }

    #endregion

    #region Native API

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint LWA_ALPHA = 0x00000002;
    private const uint WM_CLOSE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;
    private const int SW_MAXIMIZE = 3;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int GA_ROOT = 2;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool FlashWindow(IntPtr hWnd, bool bInvert);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    #endregion

    #region 新增酷炫功能

    /// <summary>
    /// 窗口截图保存
    /// </summary>
    private static void CaptureWindow(string[] args)
    {
        var hWnd = GetPickedWindow();
        var rect = GetWindowRect(hWnd);
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        
        if (width <= 0 || height <= 0)
        {
            UI.PrintError("窗口尺寸无效");
            return;
        }

        var filename = args.Length > 0 ? string.Join(" ", args) : $"winshot_{DateTime.Now:MMdd_HHmmss}.png";
        if (!filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            filename += ".png";

        try
        {
            using var bmp = new System.Drawing.Bitmap(width, height);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                // 使用 PrintWindow 截取窗口，支持后台窗口
                var hdc = g.GetHdc();
                var windowDC = GetWindowDC(hWnd);
                BitBlt(hdc, 0, 0, width, height, windowDC, 0, 0, 0x00CC0020); // SRCCOPY
                g.ReleaseHdc(hdc);
                ReleaseDC(hWnd, windowDC);
            }

            // 保存到桌面
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var filepath = Path.Combine(desktop, filename);
            // 处理重名
            int counter = 1;
            var originalFilepath = filepath;
            while (File.Exists(filepath))
            {
                var name = Path.GetFileNameWithoutExtension(originalFilepath);
                filepath = Path.Combine(desktop, $"{name}_{counter}.png");
                counter++;
            }

            bmp.Save(filepath, System.Drawing.Imaging.ImageFormat.Png);
            UI.PrintSuccess($"截图已保存: {filepath}");
        }
        catch (Exception ex)
        {
            UI.PrintError($"截图失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 切换点击穿透模式（透明+鼠标穿透）
    /// </summary>
    private static void ToggleClickThrough()
    {
        var hWnd = GetPickedWindow();
        var exStyle = (uint)GetWindowLong(hWnd, GWL_EXSTYLE);
        bool isClickThrough = (exStyle & WS_EX_TRANSPARENT) != 0 && (exStyle & WS_EX_LAYERED) != 0;

        if (isClickThrough)
        {
            // 恢复正常
            SetWindowLong(hWnd, GWL_EXSTYLE, (int)(exStyle & ~WS_EX_TRANSPARENT & ~WS_EX_LAYERED));
            SetLayeredWindowAttributes(hWnd, 0, 255, LWA_ALPHA);
            UI.PrintSuccess($"{_pickedTitle} → 恢复正常模式");
        }
        else
        {
            // 设置点击穿透 + 透明
            SetWindowLong(hWnd, GWL_EXSTYLE, (int)(exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED));
            SetLayeredWindowAttributes(hWnd, 0, 180, LWA_ALPHA); // 70% 透明度
            UI.PrintSuccess($"{_pickedTitle} → 点击穿透模式 (70%透明，鼠标可穿透)");
            UI.PrintInfo("提示：适合看视频/文档时置顶但不挡操作");
        }
    }

    /// <summary>
    /// 放大镜跟随（创建一个放大镜窗口跟随鼠标）
    /// </summary>
    private static void ShowMagnifier()
    {
        UI.PrintInfo("放大镜已启动 - 按任意键关闭");
        UI.PrintInfo("提示：移动鼠标即可放大查看");
        
        // 创建放大镜窗口
        var magnifierSize = 200;
        var zoomLevel = 2.0f;
        
        // 使用 Windows 内置放大镜 API
        try
        {
            // 启动 Windows 放大镜
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "magnify.exe",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            
            UI.PrintSuccess("已启动系统放大镜 (magnify.exe)");
            UI.PrintInfo("你可以按 Win + + 放大，Win + - 缩小，Win + Esc 关闭");
        }
        catch (Exception ex)
        {
            UI.PrintError($"启动放大镜失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 克隆窗口 - 尝试再开一个同款应用
    /// </summary>
    private static void CloneWindow()
    {
        var hWnd = GetPickedWindow();
        GetWindowThreadProcessId(hWnd, out uint pid);
        
        try
        {
            var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            var procName = proc.ProcessName;
            var exePath = proc.MainModule?.FileName;
            
            if (string.IsNullOrEmpty(exePath))
            {
                UI.PrintError("无法获取程序路径");
                return;
            }

            // 特殊处理：浏览器类应用使用新窗口参数
            var args = procName.ToLowerInvariant() switch
            {
                "chrome" => "--new-window",
                "firefox" => "-new-window",
                "msedge" => "--new-window",
                "code" => "-n", // VS Code 新窗口
                _ => ""
            };

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = true
            };
            
            System.Diagnostics.Process.Start(startInfo);
            UI.PrintSuccess($"已克隆: {procName}");
            if (!string.IsNullOrEmpty(args))
                UI.PrintInfo($"使用参数: {args}");
        }
        catch (Exception ex)
        {
            UI.PrintError($"克隆失败: {ex.Message}");
            UI.PrintInfo("提示：某些UWP应用或受保护程序无法克隆");
        }
    }

    #endregion

    /// <summary>
    /// 钉住窗口 - 置顶贴图模式
    /// </summary>
    private static void TogglePinWindow()
    {
        var hWnd = GetPickedWindow();
        var exStyle = (uint)GetWindowLong(hWnd, GWL_EXSTYLE);
        bool isPinned = (exStyle & WS_EX_TOPMOST) != 0 && _pinnedWindows.Contains(hWnd);

        if (isPinned)
        {
            // 取消钉住
            SetWindowPos(hWnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            // 恢复标题栏
            var style = (uint)GetWindowLong(hWnd, GWL_STYLE);
            SetWindowLong(hWnd, GWL_STYLE, (int)(style | WS_CAPTION | WS_THICKFRAME));
            SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
            _pinnedWindows.Remove(hWnd);
            UI.PrintSuccess($"{_pickedTitle} → 取消钉住");
        }
        else
        {
            // 钉住窗口 - 置顶 + 无边框 + 无法最小化
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            _pinnedWindows.Add(hWnd);
            UI.PrintSuccess($"{_pickedTitle} → 已钉住(置顶贴图模式)");
            UI.PrintInfo("提示：窗口已置顶，再次执行 win pin 取消");
        }
    }

    /// <summary>
    /// 窗口背景模糊效果（亚克力/毛玻璃）
    /// </summary>
    private static void ToggleBlurWindow()
    {
        var hWnd = GetPickedWindow();
        
        try
        {
            // 检查是否已启用模糊
            bool isBlurred = _blurredWindows.Contains(hWnd);
            
            if (isBlurred)
            {
                // 关闭模糊效果
                var accent = new AccentPolicy { AccentState = AccentState.ACCENT_DISABLED };
                var accentStructSize = Marshal.SizeOf(accent);
                var accentPtr = Marshal.AllocHGlobal(accentStructSize);
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = accentStructSize
                };

                SetWindowCompositionAttribute(hWnd, ref data);
                Marshal.FreeHGlobal(accentPtr);
                
                // 恢复窗口背景
                SetWindowLong(hWnd, GWL_EXSTYLE, (int)(GetWindowLong(hWnd, GWL_EXSTYLE) & ~WS_EX_TRANSPARENT));
                
                _blurredWindows.Remove(hWnd);
                UI.PrintSuccess($"{_pickedTitle} → 关闭模糊效果");
            }
            else
            {
                // 启用亚克力模糊效果
                var accent = new AccentPolicy
                {
                    AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    AccentFlags = 2,
                    GradientColor = 0x99FFFFFF  // 半透明白色背景
                };
                
                var accentStructSize = Marshal.SizeOf(accent);
                var accentPtr = Marshal.AllocHGlobal(accentStructSize);
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = accentStructSize
                };

                SetWindowCompositionAttribute(hWnd, ref data);
                Marshal.FreeHGlobal(accentPtr);
                
                // 添加透明样式使效果更明显
                SetWindowLong(hWnd, GWL_EXSTYLE, (int)(GetWindowLong(hWnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT));
                
                _blurredWindows.Add(hWnd);
                UI.PrintSuccess($"{_pickedTitle} → 启用亚克力模糊效果");
                UI.PrintInfo("提示：再次执行 win blur 关闭效果");
            }
        }
        catch (Exception ex)
        {
            UI.PrintError($"模糊效果设置失败: {ex.Message}");
            UI.PrintInfo("提示：此功能需要 Windows 10 1803+ 或 Windows 11");
        }
    }

    #region Native API (新增)

    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private static readonly HashSet<IntPtr> _pinnedWindows = new();
    private static readonly HashSet<IntPtr> _blurredWindows = new();

    // 窗口合成属性
    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);
    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    #endregion
}
