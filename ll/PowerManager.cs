using System.Diagnostics;
using System.Runtime.InteropServices;
using LL.Native;

namespace LL;

public static class PowerManager
{
    private static CancellationTokenSource? _shutdownCts;
    private static Task? _shutdownTask;
    public static DateTime? TargetTime { get; private set; }
    public static string? CurrentMode { get; private set; }

    public static void StartShutdownSequence(string[] args)
    {
        if (_shutdownTask != null && !_shutdownTask.IsCompleted)
        {
            UI.PrintError("已有任务正在运行。使用 'sdst' 查看状态，或 'sdc' 取消。");
            return;
        }

        if (args.Length > 0 && (args[0] == "?" || args[0].ToLower() == "help"))
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
        CurrentMode = "Countdown";

        UI.PrintHeader("SHUTDOWN SEQUENCE INITIATED");
        UI.PrintResult("Duration", duration.ToString(@"hh\:mm\:ss"));
        UI.PrintResult("Target Time", TargetTime.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        UI.PrintInfo("倒计时已在后台运行 (查看窗口标题)");
        UI.PrintInfo("命令行可继续使用。输入 'sdc' 可取消。");

        _shutdownCts = new CancellationTokenSource();
        var token = _shutdownCts.Token;

        _shutdownTask = Task.Run(async () =>
        {
            string originalTitle = Console.Title;
            try
            {
                while (DateTime.Now < TargetTime)
                {
                    token.ThrowIfCancellationRequested();
                    var remaining = TargetTime.Value - DateTime.Now;
                    if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

                    Console.Title = $"LL - Shutdown in {remaining:hh\\:mm\\:ss}";
                    await Task.Delay(1000, token);
                }

                Console.Title = originalTitle;
                Utils.SendEmailTo($"Shutdown initiated automatically at {DateTime.Now}");
                ExecuteShutdown();
            }
            catch (OperationCanceledException)
            {
                Console.Title = originalTitle;
            }
            catch (Exception ex)
            {
                Console.Title = originalTitle;
                LogException(ex);
            }
        });
    }

    public static void StartIdleMonitor(string[] args)
    {
        if (_shutdownTask != null && !_shutdownTask.IsCompleted)
        {
            UI.PrintError("已有任务正在运行。使用 'sdst' 查看状态，或 'sdc' 取消。");
            return;
        }

        double idleThresholdSeconds = 7200; // 默认2小时

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

        CurrentMode = "IdleMonitor";
        TargetTime = null; // Idle mode doesn't have a fixed target time initially

        UI.PrintHeader("IDLE MONITOR ACTIVATED");
        UI.PrintResult("Idle Threshold", TimeSpan.FromSeconds(idleThresholdSeconds).ToString(@"hh\:mm\:ss"));
        UI.PrintInfo("正在监听系统空闲状态... 无操作达时限将自动关机。");
        UI.PrintInfo("命令行可继续使用。输入 'sdc' 可取消监听。");

        _shutdownCts = new CancellationTokenSource();
        var token = _shutdownCts.Token;

        _shutdownTask = Task.Run(async () =>
        {
            string originalTitle = Console.Title;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    uint idleTimeMs = GetIdleTime();
                    double idleSeconds = idleTimeMs / 1000.0;
                    
                    if (idleSeconds >= idleThresholdSeconds)
                    {
                        Console.Title = originalTitle;
                        UI.PrintInfo($"检测到长时间无人使用 ({TimeSpan.FromSeconds(idleSeconds):hh\\:mm\\:ss})，执行自动关机。");
                        Utils.SendEmailTo($"Idle shutdown initiated at {DateTime.Now} after {idleSeconds}s of inactivity.");
                        ExecuteShutdown();
                        break;
                    }

                    Console.Title = $"LL - Idle: {TimeSpan.FromSeconds(idleSeconds):hh\\:mm\\:ss} / {TimeSpan.FromSeconds(idleThresholdSeconds):hh\\:mm\\:ss}";
                    await Task.Delay(2000, token);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                Console.Title = originalTitle;
            }
        });
    }

    public static void ShowStatus()
    {
        if (_shutdownTask == null || _shutdownTask.IsCompleted)
        {
            UI.PrintInfo("当前没有正在运行的电源管理任务。");
            return;
        }

        UI.PrintResult("当前模式", CurrentMode ?? "Unknown");

        if (CurrentMode == "Countdown" && TargetTime.HasValue)
        {
            var remaining = TargetTime.Value - DateTime.Now;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            UI.PrintResult("剩余时间", remaining.ToString(@"hh\:mm\:ss"));
            UI.PrintResult("目标时间", TargetTime.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        else if (CurrentMode == "IdleMonitor")
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
        }
        else
        {
            UI.PrintInfo("当前没有正在运行的任务。");
        }
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
            return (uint)Environment.TickCount - lastInputInfo.dwTime;
        }
        return 0;
    }

    private static void PrintShutdownHelp()
    {
        UI.PrintInfo("=== 倒计时关机使用说明 ===");
        UI.PrintInfo("基本用法: sd [时间][单位]");
        UI.PrintInfo("  sd        -> 进入交互设置模式");
        UI.PrintInfo("  sd 30m    -> 30 分钟");
        UI.PrintInfo("  sd 1h     -> 1 小时");
        UI.PrintInfo("停止/管理:");
        UI.PrintInfo("  sdst      -> 查看状态");
        UI.PrintInfo("  sdc       -> 取消倒计时");
    }

    private static void LogException(Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "log.txt"), $"{DateTime.Now}: {ex.Message}\r\n");
        }
        catch { }
    }
}
