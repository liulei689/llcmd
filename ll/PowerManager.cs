using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LL.Native;

namespace LL;

public static class PowerManager
{
    // 可调参数（从 config.json 读取）
    public static double IdleEnterSeconds { get; private set; } = 3*60; // 空闲超过多少秒进入“空闲状态”(用于触发守护/锁屏等)
    public static double IdleExitSeconds { get; private set; } = 1;   // 空闲低于多少秒认为恢复操作
    public static double IdleLockSeconds { get; private set; } = 4*60;  // 空闲达到多少秒自动锁屏

    static PowerManager()
    {
        LoadConfig();
    }

    private static void LoadConfig()
    {
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("PowerManager", out var powerManager))
                {
                    if (powerManager.TryGetProperty("IdleEnterSeconds", out var idleEnter))
                        IdleEnterSeconds = idleEnter.GetDouble();
                    if (powerManager.TryGetProperty("IdleExitSeconds", out var idleExit))
                        IdleExitSeconds = idleExit.GetDouble();
                    if (powerManager.TryGetProperty("IdleLockSeconds", out var idleLock))
                        IdleLockSeconds = idleLock.GetDouble();
                }
            }
        }
        catch (Exception ex)
        {
            // 静默失败，使用默认值
            LogManager.Log("Error", "PowerManager", $"Failed to load config: {ex.Message}");
        }
    }

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
                        // 记录守护模式激活的时间
                        DateTime guardianActivatedTime = DateTime.Now;
                        // 等待一段时间，避免守护模式界面的显示和更新导致空闲时间被重置
                        await Task.Delay(2000, token); // 等待2秒
                    }
                    else if (!isIdleMode && guardianAutoActive)
                    {
                        // 检查是否真的有用户输入，而不是因为守护模式界面的显示和更新
                        // 只有当空闲时间确实很小（小于 IdleExitSeconds）并且持续一段时间后，才认为是真正的用户输入
                        bool hasRealUserInput = false;
                        int consecutiveLowIdleCount = 0;
                        const int requiredConsecutiveChecks = 10; // 需要连续10次检查都显示空闲时间很低
                        
                        for (int i = 0; i < requiredConsecutiveChecks; i++)
                        {
                            uint currentIdleMs = GetIdleTime();
                            double currentIdleSeconds = currentIdleMs / 1000.0;
                            if (currentIdleSeconds < IdleExitSeconds)
                            {
                                consecutiveLowIdleCount++;
                            }
                            else
                            {
                                break;
                            }
                            await Task.Delay(200, token); // 每次检查间隔200毫秒
                        }
                        
                        hasRealUserInput = consecutiveLowIdleCount >= requiredConsecutiveChecks;
                        
                        if (hasRealUserInput)
                        {
                            guardianAutoActive = false;
                            // 空闲时间自动退出守护模式也需要面部识别验证
                            if (GuardianManager.IsActive)
                            {
                                // 先异步启动面部识别程序（让窗口尽快出现）
                                var authTask = Task.Run(() => FaceAuthCommands.Authenticate(), token);
                                
                                // 同时显示全屏进度条
                                await ShowFaceAuthProgressScreen("空闲结束 - 面部识别验证", token);
                                
                                // 等待面部识别完成
                                var result = await authTask;
                                if (result.Success)
                                {
                                    UI.PrintSuccess($"面部识别通过，退出守护模式");
                                    GuardianManager.AutoExitGuardianMode();
                                }
                                else
                                {
                                    UI.PrintError($"面部识别验证失败: {result.ErrorMessage}");
                                    UI.PrintInfo("继续守护模式");
                                    // 验证失败，重新进入空闲模式计数
                                    guardianAutoActive = true;
                                }
                            }
                        }
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

    /// <summary>
    /// 显示全屏进度条（5秒，无闪烁）
    /// </summary>
    private static async Task ShowFaceAuthProgressScreen(string title, CancellationToken token)
    {
        Console.Clear();
        Console.CursorVisible = false;
        
        int w = Console.WindowWidth;
        int h = Console.WindowHeight;
        int centerY = h / 2;
        int barWidth = Math.Min(60, w - 20);
        
        // 初始化界面（只画一次边框）
        Console.ForegroundColor = ConsoleColor.White;
        Console.SetCursorPosition((w - title.Length) / 2, centerY - 4);
        Console.Write(title);
        
        string subtitle = "正在启动面部识别组件，请稍候...";
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.SetCursorPosition((w - subtitle.Length) / 2, centerY - 2);
        Console.Write(subtitle);
        
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.SetCursorPosition((w - barWidth - 2) / 2, centerY);
        Console.Write($"╔{new string('═', barWidth)}╗");
        Console.SetCursorPosition((w - barWidth - 2) / 2, centerY + 2);
        Console.Write($"╚{new string('═', barWidth)}╝");
        Console.SetCursorPosition((w - barWidth - 2) / 2, centerY + 1);
        Console.Write($"║{new string('░', barWidth)}║ 0%");
        Console.ResetColor();
        
        // 5秒进度条动画（只更新进度部分）
        int[] delays = new[] { 80, 80, 70, 70, 60, 60, 50, 50, 40, 50,
                               40, 40, 50, 40, 40, 50, 40, 40, 50, 40,
                               40, 40, 50, 40, 40, 40, 50, 40, 40, 40,
                               50, 40, 40, 50, 40, 40, 50, 40, 50, 60,
                               50, 60, 50, 60, 70, 60, 70, 80, 70, 80 };
        
        for (int percent = 0; percent <= 100; percent += 2)
        {
            int filled = (int)(barWidth * percent / 100.0);
            int empty = barWidth - filled;
            string filledPart = filled > 0 ? new string('█', filled) : "";
            string emptyPart = empty > 0 ? new string('░', empty) : "";
            string barLine = $"║{filledPart}{emptyPart}║ {percent}%";
            int pad = Math.Max(0, (w - barLine.Length) / 2);
            
            if (percent < 30)
                Console.ForegroundColor = ConsoleColor.Cyan;
            else if (percent < 70)
                Console.ForegroundColor = ConsoleColor.Yellow;
            else
                Console.ForegroundColor = ConsoleColor.Green;
                
            Console.SetCursorPosition(pad, centerY + 1);
            Console.Write(barLine);
            Console.ResetColor();
            
            int delayIndex = percent / 2;
            await Task.Delay(delayIndex < delays.Length ? delays[delayIndex] : 50, token);
        }
        
        // 完成提示 - 显示在进度条下方，不清屏
        string done = "正在启动面部识别程序...";
        Console.ForegroundColor = ConsoleColor.Green;
        Console.SetCursorPosition((w - done.Length) / 2, centerY + 4);
        Console.Write(done);
        
        string hint = "（请面对摄像头）";
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.SetCursorPosition((w - hint.Length) / 2, centerY + 6);
        Console.Write(hint);
        
        // 等待一小段时间让用户看到提示，给面部识别程序启动时间
        await Task.Delay(1500, token);
        
        Console.ResetColor();
        // 注意：这里不清屏，让面部识别程序自己覆盖显示
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
            // 解决 Environment.TickCount 溢出问题
            uint tickCount = (uint)Environment.TickCount;
            uint currentIdle;
            if (tickCount >= lastInputInfo.dwTime)
            {
                currentIdle = tickCount - lastInputInfo.dwTime;
            }
            else
            {
                // 溢出情况：计算正确的空闲时间
                currentIdle = (uint.MaxValue - lastInputInfo.dwTime) + tickCount + 1;
            }
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
        // 方法1：通过前台窗口类名判断
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
        {
            StringBuilder className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            string cls = className.ToString();
            if (cls.Contains("LockScreen") || cls.Contains("Windows.UI.Core.CoreWindow") || cls.Contains("Credential"))
            {
                return true;
            }
        }

        // 方法2：通过 WTS 会话信息判断
        try
        {
            IntPtr server = IntPtr.Zero;
            int sessionId = -1;
            IntPtr buffer = IntPtr.Zero;
            int bytesReturned = 0;

            // 获取当前会话ID
            if (NativeMethods.ProcessIdToSessionId((uint)Process.GetCurrentProcess().Id, ref sessionId))
            {
                // 查询会话连接状态
                if (WTSQuerySessionInformation(server, sessionId, WTS_INFO_CLASS.WTSConnectState, out buffer, out bytesReturned) && buffer != IntPtr.Zero)
                {
                    WTS_CONNECTSTATE_CLASS state = (WTS_CONNECTSTATE_CLASS)Marshal.ReadInt32(buffer);
                    WTSFreeMemory(buffer);
                    // 如果会话状态是锁定或断开连接，则认为屏幕被锁定
                    if (state == WTS_CONNECTSTATE_CLASS.WTSDisconnected || state == WTS_CONNECTSTATE_CLASS.WTSIdle)
                    {
                        return true;
                    }
                }
            }
        }
        catch { }

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
