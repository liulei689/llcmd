using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LL.Native;

namespace LL;

public static class PowerManager
{
    // 可调参数（按需修改）
    public static double IdleEnterSeconds { get; set; } = 3; // 空闲超过多少秒进入“空闲状态”(用于触发守护/锁屏等)
    public static double IdleExitSeconds { get; set; } = 1;   // 空闲低于多少秒认为恢复操作
    public static double IdleLockSeconds { get; set; } = 5;  // 空闲达到多少秒自动锁屏

    private static CancellationTokenSource? _shutdownCts;
    private static Task? _shutdownTask;
    public static DateTime? TargetTime { get; private set; }
    public static string? CurrentMode { get; private set; }

    private static uint _lastIdleTime = 0;
    private static DateTime _lastCheckTime = DateTime.Now;

    public static void StartShutdownSequence(string[] args)
    {
        if (_shutdownTask != null && !_shutdownTask.IsCompleted)
        {
            UI.PrintError("已有任务正在运行。使用 'st' 查看状态，或 'c' 取消。");
            return;
        }

        if (args.Length > 0 && (args[0] == "?" || args[0].Equals("help", StringComparison.CurrentCultureIgnoreCase)))
        {
            PrintShutdownHelp();
            return;
        }

        double totalSeconds = 7200; // 默认2小时

        if (args.Length > 0)
        {
            if (!Utils.TryParseTime(args[0], out totalSeconds))
            {
                UI.PrintError($"时间格式错误: '{args[0]}'");
                return;
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("请输入倒计时时间 (默认 2h, 直接回车使用默认值): ");
            Console.ResetColor();
            string? input = Console.ReadLine();

            if (!string.IsNullOrWhiteSpace(input))
            {
                if (!Utils.TryParseTime(input, out totalSeconds))
                {
                    UI.PrintError("时间格式无效，操作已取消。");
                    return;
                }
            }
        }

        TimeSpan duration = TimeSpan.FromSeconds(totalSeconds);
        TargetTime = DateTime.Now.Add(duration);
        CurrentMode = "倒计时关机";

        UI.PrintHeader("关机程序已启动");
        UI.PrintResult("时长", duration.ToString(@"hh\:mm\:ss"));
        UI.PrintResult("目标时间", TargetTime.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        UI.PrintInfo("倒计时已在后台运行 (查看窗口标题)");
        UI.PrintInfo("命令行可继续使用。输入 'c' 可取消。");

        _shutdownCts = new CancellationTokenSource();
        var localCts = _shutdownCts;
        TaskManager.Register("倒计时关机", localCts);
        var token = localCts.Token;

        _shutdownTask = Task.Run(async () =>
        {
            string originalTitle = string.Empty;
            if (OperatingSystem.IsWindows())
                originalTitle = Console.Title;
            try
            {
                while (DateTime.Now < TargetTime)
                {
                    token.ThrowIfCancellationRequested();
                    var remaining = TargetTime.Value - DateTime.Now;
                    if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

                    if (OperatingSystem.IsWindows())
                        Program.ShutdownTimeDisplay = $"剩余时间 {remaining:hh\\:mm\\:ss}";
                        Program.UpdateConsoleTitle();
                    await Task.Delay(1000, token);
                }

                if (OperatingSystem.IsWindows())
                    Console.Title = originalTitle;
                Utils.SendEmailTo("系统通知 - 倒计时关机", $"系统于 {DateTime.Now} 在 {Environment.MachineName} 上自动启动关机程序");
                LogManager.Log("Info", "System", $"自动关机 - {Environment.MachineName}");
                Program.LockStartTime = null;
                ExecuteShutdown();
            }
            catch (OperationCanceledException)
            {
                if (OperatingSystem.IsWindows())
                    Program.ShutdownTimeDisplay = "";
                    Program.UpdateConsoleTitle();
            }
            catch (Exception ex)
            {
                if (OperatingSystem.IsWindows())
                    Program.ShutdownTimeDisplay = "";
                    Program.UpdateConsoleTitle();
                LogException(ex);
            }
            finally
            {
                TaskManager.Clear(localCts);
            }
        });
    }

    public static void StartIdleMonitor(string[] args)
    {
        if (_shutdownTask != null && !_shutdownTask.IsCompleted)
        {
            UI.PrintError("已有任务正在运行。使用 'st' 查看状态，或 'c' 取消。");
            return;
        }

        double idleThresholdSeconds = 7200; // 默认2小时 (触发关机)

        if (args.Length > 0)
        {
            if (!Utils.TryParseTime(args[0], out idleThresholdSeconds))
            {
                UI.PrintError($"时间格式错误: '{args[0]}'");
                return;
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("请输入无人操作触发时长 (默认 2h, 直接回车使用默认值): ");
            Console.ResetColor();
            string? input = Console.ReadLine();

            if (!string.IsNullOrWhiteSpace(input))
            {
                if (!Utils.TryParseTime(input, out idleThresholdSeconds))
                {
                    UI.PrintError("时间格式无效，操作已取消。");
                    return;
                }
            }
        }

        CurrentMode = "空闲关机监听";
        TargetTime = null; // Idle mode doesn't have a fixed target time initially

        UI.PrintHeader("空闲关机监听已激活");
        UI.PrintResult("空闲阈值", TimeSpan.FromSeconds(idleThresholdSeconds).ToString(@"hh\:mm\:ss"));
        UI.PrintInfo("正在监听系统空闲状态... 无操作达时限将自动关机。");
        UI.PrintInfo("命令行可继续使用。输入 'c' 可取消监听。");

        _shutdownCts = new CancellationTokenSource();
        var localCts = _shutdownCts;
        TaskManager.Register("空闲关机监听", localCts);
        var token = localCts.Token;

        _shutdownTask = Task.Run(async () =>
        {
            string originalTitle = string.Empty;
            if (OperatingSystem.IsWindows())
                originalTitle = Console.Title;
            try
            {
                bool isIdleMode = false;
                bool guardianAutoActive = false;
                bool lockedThisIdleSession = false;
                int lastLoggedIdleMinutes = -1; // Track last logged idle minutes

                while (!token.IsCancellationRequested)
                {
                    uint idleTimeMs = GetIdleTime();
                    double idleSeconds = idleTimeMs / 1000.0;

                    var idle = TimeSpan.FromMilliseconds(idleTimeMs);
                    var threshold = TimeSpan.FromSeconds(idleThresholdSeconds);

                    // Idle mode state machine (for UI/monitoring only)
                    if (!isIdleMode && idleSeconds >= IdleEnterSeconds)
                    {
                        isIdleMode = true;
                        lockedThisIdleSession = false;
                    }
                    else if (isIdleMode && idleSeconds < IdleExitSeconds)
                    {
                        isIdleMode = false;
                        lockedThisIdleSession = false;
                    }

                    if (!lockedThisIdleSession && idleSeconds >= IdleLockSeconds)
                    {
                        lockedThisIdleSession = true;
                        try
                        {
                            NativeMethods.LockWorkStation();
                            Program.LockStartTime = DateTime.Now;
                            Utils.SendEmailTo("系统通知 - 自动锁屏", $"系统检测到长时间无人操作，已自动锁屏。当前空闲时间: {idle:hh\\:mm\\:ss}，机器: {Environment.MachineName}");
                            LogManager.Log("Info", "System", $"自动锁屏，空闲时间: {idle:hh\\:mm\\:ss} - {Environment.MachineName}");
                        }
                        catch { }
                    }

                    // Auto toggle Guardian mode only if Guardian is not manually enabled.
                    if (isIdleMode && !guardianAutoActive && !GuardianManager.IsActive)
                    {
                        guardianAutoActive = true;
                        GuardianManager.ToggleGuardianMode([]);
                    }
                    else if (!isIdleMode && guardianAutoActive)
                    {
                        guardianAutoActive = false;
                        // Only auto-exit what we auto-entered.
                        if (GuardianManager.IsActive)
                            GuardianManager.ToggleGuardianMode([]);
                    }
                    
                    if (idleSeconds >= idleThresholdSeconds)
                    {
                        if (OperatingSystem.IsWindows())
                            Console.Title = originalTitle;
                        UI.PrintInfo($"检测到长时间无人使用 ({TimeSpan.FromSeconds(idleSeconds):hh\\:mm\\:ss})，执行自动关机。");
                        Utils.SendEmailTo("系统通知 - 自动关机", $"系统检测到长时间无人操作，已执行自动关机。空闲时间: {idle:hh\\:mm\\:ss}，机器: {Environment.MachineName}");
                        LogManager.Log("Info", "System", $"因长时间无操作自动关机，空闲时间: {idle:hh\\:mm\\:ss} - {Environment.MachineName}");
                        Program.LockStartTime = null;
                        ExecuteShutdown();
                        break;
                    }

                    // Log idle time every minute increase
                    int currentIdleMinutes = (int)idle.TotalMinutes;
                    if (currentIdleMinutes > lastLoggedIdleMinutes)
                    {
                        LogManager.Log("Info", "System", $"空闲时间更新: {idle:hh\\:mm\\:ss}");
                        lastLoggedIdleMinutes = currentIdleMinutes;
                    }

                    // Always show full idle time (minutes keep increasing; not stuck at 59s)
                    // Keep UI quiet: only reflect state in title.
                    int guardianCountdown = Math.Max(0, (int)(IdleEnterSeconds - idleSeconds));
                    int lockCountdown = Math.Max(0, (int)(IdleLockSeconds - idleSeconds));
                    Program.GuardianCountdown = guardianCountdown;
                    Program.LockCountdown = lockCountdown;
                    if (OperatingSystem.IsWindows())
                        Program.IdleTimeDisplay = $"空闲/关机: {idle:hh\\:mm\\:ss} / {threshold:hh\\:mm\\:ss}";
                        Program.UpdateConsoleTitle();
                    await Task.Delay(200, token);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (OperatingSystem.IsWindows())
                    Program.IdleTimeDisplay = "";
                    Program.UpdateConsoleTitle();

                TaskManager.Clear(localCts);
            }
        });
    }

    public static void ShowStatus()
    {
        var (name, startedAt) = TaskManager.GetLatest();
        if (!string.IsNullOrWhiteSpace(name))
        {
            UI.PrintResult("可取消任务", startedAt.HasValue ? $"{name} (开始于 {startedAt:HH:mm:ss})" : name);
        }

        if (_shutdownTask == null || _shutdownTask.IsCompleted)
        {
            UI.PrintInfo("当前没有正在运行的电源管理任务。");
            return;
        }

        UI.PrintResult("当前模式", CurrentMode ?? "未知");

        if (CurrentMode == "倒计时关机" && TargetTime.HasValue)
        {
            var remaining = TargetTime.Value - DateTime.Now;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            UI.PrintResult("剩余时间", remaining.ToString(@"hh\:mm\:ss"));
            UI.PrintResult("目标时间", TargetTime.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        else if (CurrentMode == "空闲关机监听")
        {
            uint idleTimeMs = GetIdleTime();
            UI.PrintResult("当前空闲", TimeSpan.FromMilliseconds(idleTimeMs).ToString(@"hh\:mm\:ss"));
        }
    }

    public static void CancelTask()
    {
        if (_shutdownCts != null && !_shutdownCts.IsCancellationRequested)
        {
            _shutdownCts.Cancel();
            UI.PrintSuccess($"已取消运行中的任务 ({CurrentMode})。");
            return;
        }

        UI.PrintInfo("当前没有正在运行的电源管理任务。");
    }

    public static void AbortSystemShutdown()
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true, UseShellExecute = false });
            UI.PrintSuccess("已发送取消系统关机指令 (Sent shutdown abort command)");
        }
        catch (Exception ex)
        {
            UI.PrintError($"执行失败: {ex.Message}");
        }
    }

    private static void ExecuteShutdown()
    {
        Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
    }

    private static uint GetIdleTime()
    {
        var lastInputInfo = new NativeMethods.LASTINPUTINFO();
        lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);
        if (NativeMethods.GetLastInputInfo(ref lastInputInfo))
        {
            uint currentIdle = (uint)Environment.TickCount - lastInputInfo.dwTime;
            var now = DateTime.Now;

            if (IsScreenLocked())
            {
                // 锁屏状态：累加空闲时间，不重置
                uint elapsedMs = (uint)(now - _lastCheckTime).TotalMilliseconds;
                _lastIdleTime += elapsedMs;
            }
            else
            {
                // 非锁屏：正常计算
                _lastIdleTime = currentIdle;
            }

            _lastCheckTime = now;
            return _lastIdleTime;
        }
        return 0;
    }

    private static bool IsScreenLocked()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
        {
            StringBuilder className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            string cls = className.ToString();
            if (cls.Contains("LockScreen") || cls.Contains("Windows.UI.Core.CoreWindow"))
            {
                LogManager.Log("Info", "System", $"锁屏检测: 前台窗口类名={cls}, 锁屏=true");
                return true;
            }
        }
        return false;
    }

    private static void PrintShutdownHelp()
    {
        UI.PrintInfo("=== 关机管理说明 ===");
        UI.PrintInfo(" sd 30m   -> 30分钟后关机");
        UI.PrintInfo(" idle 1h  -> 闲置1小时关机");
        UI.PrintInfo(" st       -> 查看状态");
        UI.PrintInfo(" c        -> 取消任务");
    }

    private static void LogException(Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "log.txt"), $"{DateTime.Now}: {ex.Message}\r\n");
        }
        catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("wtsapi32.dll", CharSet = CharSet.Auto)]
    private static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, WTS_INFO_CLASS wtsInfoClass, out IntPtr ppBuffer, out int pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    private enum WTS_INFO_CLASS
    {
        WTSInitialProgram = 0,
        WTSApplicationName = 1,
        WTSWorkingDirectory = 2,
        WTSOEMId = 3,
        WTSSessionId = 4,
        WTSUserName = 5,
        WTSWinStationName = 6,
        WTSDomainName = 7,
        WTSConnectState = 8,
        WTSClientBuildNumber = 9,
        WTSClientName = 10,
        WTSClientDirectory = 11,
        WTSClientProductId = 12,
        WTSClientHardwareId = 13,
        WTSClientAddress = 14,
        WTSClientDisplay = 15,
        WTSClientProtocolType = 16,
        WTSIdleTime = 17,
        WTSLogonTime = 18,
        WTSIncomingBytes = 19,
        WTSOutgoingBytes = 20,
        WTSIncomingFrames = 21,
        WTSOutgoingFrames = 22,
        WTSClientInfo = 23,
        WTSSessionInfo = 24,
        WTSSessionInfoEx = 25,
        WTSConfigInfo = 26,
        WTSValidationInfo = 27,
        WTSSessionAddressV4 = 28,
        WTSIsRemoteSession = 29
    }

    private enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit
    }

    private const uint DESKTOP_SWITCHDESKTOP = 0x0100;
}
