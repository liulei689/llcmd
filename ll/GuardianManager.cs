using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using System.IO;
using System.Text;
using System.Collections.Concurrent;
using LL.Native;

namespace LL;

public static class GuardianManager
{
    private static bool _isActive = false;
    private static CancellationTokenSource? _effectCts;
    private static readonly Process _currentProcess = Process.GetCurrentProcess();
    private static readonly object _lock = new();

    private static readonly ConcurrentQueue<string> _events = new();
    private static readonly SemaphoreSlim _eventsSignal = new(0);
    private static Task? _keyTask;
    private static Task? _eventTask;

    private static readonly Dictionary<string, DateTime> _lastEventAt = new();
    private static readonly object _eventGate = new();

    public static void ToggleGuardianMode(string[] args)
    {
        lock (_lock)
        {
            if (_isActive)
            {
                StopGuardianMode();
                Utils.SendEmailTo("系统通知 - 守护模式", $"系统于 {DateTime.Now} 在 {Environment.MachineName} 上退出守护模式");
                LogManager.Log("Info", "System", $"退出守护模式 - {Environment.MachineName}");
            }
            else
            {
                StartGuardianMode();
                Utils.SendEmailTo("系统通知 - 守护模式", $"系统于 {DateTime.Now} 在 {Environment.MachineName} 上进入守护模式");
                LogManager.Log("Info", "System", $"进入守护模式 - {Environment.MachineName}");
            }
        }
    }

    public static bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _isActive;
            }
        }
    }

    private static void StartGuardianMode()
    {
        Program.GuardianStartTime = DateTime.Now;
        _isActive = true;
        Console.CursorVisible = false;
        Console.Clear();

        IntPtr hWnd = NativeMethods.GetConsoleWindow();
        NativeMethods.SetWindowPos(hWnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
        NativeMethods.ShowWindow(hWnd, NativeMethods.SW_MAXIMIZE);

        _effectCts = new CancellationTokenSource();
        var token = _effectCts.Token;

        _keyTask = Task.Run(() => KeyListenerLoop(token), token);
        _eventTask = Task.Run(() => EventProbeLoop(token), token);
        Task.Run(() => DashboardMasterLoop(token), token);
    }

    private static void StopGuardianMode()
    {
        lock (_lock)
        {
            if (!_isActive) return;
            _isActive = false;
            _effectCts?.Cancel();
        }

        Console.ResetColor();
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n [!] 正在注销守护协议：正在 detach 核心监听钩子...");

        IntPtr hWnd = NativeMethods.GetConsoleWindow();
        NativeMethods.SetWindowPos(hWnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);

        NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
        Thread.Sleep(200);
        NativeMethods.ShowWindow(hWnd, NativeMethods.SW_MINIMIZE);

        Console.CursorVisible = true;
        Console.Clear();
        UI.PrintSuccess("守护模式已关闭。系统转入后台静默监听。");
        Program.GuardianStartTime = null;
    }

    private static void EnqueueEvent(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // Dedup/throttle by category: same category within 3s will be dropped.
        var key = GetEventKey(text);
        var now = DateTime.UtcNow;
        lock (_eventGate)
        {
            if (_lastEventAt.TryGetValue(key, out var last) && (now - last).TotalSeconds < 3)
                return;
            _lastEventAt[key] = now;
            if (_lastEventAt.Count > 256)
            {
                // best-effort cleanup
                var oldKeys = _lastEventAt.Where(kv => (now - kv.Value).TotalSeconds > 30).Select(kv => kv.Key).ToArray();
                foreach (var k in oldKeys) _lastEventAt.Remove(k);
            }
        }

        _events.Enqueue(text);
        _eventsSignal.Release();
    }

    private static string GetEventKey(string text)
    {
        // Expect format: "[time] 分类：..."
        int idx = text.IndexOf(']');
        if (idx >= 0 && idx + 1 < text.Length)
        {
            var rest = text[(idx + 1)..].TrimStart();
            int sep = rest.IndexOf('：');
            if (sep > 0)
                return rest[..sep];
        }
        return "事件";
    }

    private static async Task KeyListenerLoop(CancellationToken token)
    {
        try
        {
            var cmd = new StringBuilder(8);
            while (!token.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape)
                    {
                        StopGuardianMode();
                        return;
                    }

                    // 仅用于退出：输入 gd 后回车
                    if (key.Key == ConsoleKey.Enter)
                    {
                        var text = cmd.ToString().Trim();
                        cmd.Clear();
                        if (text.Equals("gd", StringComparison.OrdinalIgnoreCase))
                        {
                            StopGuardianMode();
                            return;
                        }
                    }
                    else if (key.Key == ConsoleKey.Backspace)
                    {
                        if (cmd.Length > 0) cmd.Length -= 1;
                    }
                    else
                    {
                        char ch = key.KeyChar;
                        if (!char.IsControl(ch) && cmd.Length < 6)
                        {
                            cmd.Append(ch);
                        }
                    }
                }

                await Task.Delay(25, token);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private static async Task EventProbeLoop(CancellationToken token)
    {
        try
        {
            // Baseline samples
            var lastWs = _currentProcess.WorkingSet64;
            var lastThd = _currentProcess.Threads.Count;
            var lastHandles = _currentProcess.HandleCount;

            var lastGcBytes = GC.GetTotalMemory(false);
            var lastCpu = _currentProcess.TotalProcessorTime;
            var lastProbeAt = DateTime.UtcNow;

            var lastDriveFree = GetPrimaryDriveSafe()?.AvailableFreeSpace ?? 0;

            var nicStats = SnapshotNicBytes();

            // rotating telemetry templates (avoid the same 2-3 lines repeating)
            int teleIdx = 0;
            var startedAt = DateTime.UtcNow;

            while (!token.IsCancellationRequested)
            {
                // Process deltas
                _currentProcess.Refresh();
                var ws = _currentProcess.WorkingSet64;
                var thd = _currentProcess.Threads.Count;
                var handles = _currentProcess.HandleCount;

                if (Math.Abs(ws - lastWs) > 32L * 1024 * 1024)
                    EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 进程：内存变化 -> {Utils.FormatSize(ws)}");

                if (thd != lastThd)
                    EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 进程：线程数 {lastThd} -> {thd}");

                if (handles != lastHandles)
                    EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 进程：句柄数 {lastHandles} -> {handles}");

                lastWs = ws;
                lastThd = thd;
                lastHandles = handles;

                // GC heap (coarse, but different signal)
                var gcBytes = GC.GetTotalMemory(false);
                if (Math.Abs(gcBytes - lastGcBytes) > 64L * 1024 * 1024)
                    EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 内存：托管堆变化 -> {Utils.FormatSize(gcBytes)}");
                lastGcBytes = gcBytes;

                // CPU time delta (approx, avoids repeating the same 3 lines)
                var now = DateTime.UtcNow;
                var cpu = _currentProcess.TotalProcessorTime;
                var dt = (now - lastProbeAt).TotalMilliseconds;
                if (dt > 500)
                {
                    var cpuMs = (cpu - lastCpu).TotalMilliseconds;
                    var cpuPct = Math.Clamp(cpuMs / dt / Environment.ProcessorCount * 100.0, 0, 999);
                    if (cpuPct >= 15)
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 进程：CPU 使用率 ~ {cpuPct:F1}%");
                }
                lastCpu = cpu;
                lastProbeAt = now;

                // Network deltas (best-effort)
                var snap = SnapshotNicBytes();
                if (snap.TotalRx >= nicStats.TotalRx && snap.TotalTx >= nicStats.TotalTx)
                {
                    var rx = (snap.TotalRx - nicStats.TotalRx) / 2; // bytes/sec over 2 seconds
                    var tx = (snap.TotalTx - nicStats.TotalTx) / 2;
                    if (rx > 128 * 1024 || tx > 128 * 1024)
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 网络：下行 {Utils.FormatSize(rx)}/秒  上行 {Utils.FormatSize(tx)}/秒");
                }
                nicStats = snap;

                // Disk free (coarse)
                var drive = GetPrimaryDriveSafe();
                if (drive is not null)
                {
                    var freePct = (int)Math.Round(GetDriveFreePercent(drive));
                    if (freePct < 10)
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 磁盘：空闲不足 {freePct}%（{Utils.FormatSize(drive.AvailableFreeSpace)}）");

                    // also report large free-space changes
                    if (lastDriveFree != 0 && Math.Abs(drive.AvailableFreeSpace - lastDriveFree) > 512L * 1024 * 1024)
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 磁盘：空闲变化 -> {Utils.FormatSize(drive.AvailableFreeSpace)}");
                    lastDriveFree = drive.AvailableFreeSpace;
                }

                // Rotating telemetry lines (~20 types)
                var uptime = DateTime.UtcNow - startedAt;
                var drive2 = drive;
                var freePct2 = drive2 is null ? 0 : (int)Math.Round(GetDriveFreePercent(drive2));
                var tele = teleIdx++ % 20;
                switch (tele)
                {
                    case 0:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：进程内存 {Utils.FormatSize(ws)} / 托管堆 {Utils.FormatSize(gcBytes)}");
                        break;
                    case 1:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：线程 {thd} / 句柄 {handles}");
                        break;
                    case 2:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：磁盘空闲 {freePct2}%（{(drive2 is null ? "N/A" : Utils.FormatSize(drive2.AvailableFreeSpace))}）");
                        break;
                    case 3:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：网络累计 RX {Utils.FormatSize(snap.TotalRx)} / TX {Utils.FormatSize(snap.TotalTx)}");
                        break;
                    case 4:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：GC 次数 {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
                        break;
                    case 5:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：进程优先级 {_currentProcess.BasePriority}");
                        break;
                    case 6:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：系统核心数 {Environment.ProcessorCount}");
                        break;
                    case 7:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：系统版本 {Environment.OSVersion.Version}");
                        break;
                    case 8:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：运行时 .NET {Environment.Version}");
                        break;
                    case 9:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：在线时长 {uptime:dd\\:hh\\:mm\\:ss}");
                        break;
                    case 10:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：机器 {Environment.MachineName} / 用户 {Environment.UserName}");
                        break;
                    case 11:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：进程名 {_currentProcess.ProcessName} / PID {_currentProcess.Id}");
                        break;
                    case 12:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：托管堆(估算) {Utils.FormatSize(GC.GetTotalMemory(false))}");
                        break;
                    case 13:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：事件队列 {_events.Count}");
                        break;
                    case 14:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：磁盘监听 阈值<10% / 变化>512MB");
                        break;
                    case 15:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：网络监听 阈值>128KB/s");
                        break;
                    case 16:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：进程监听 内存>32MB变动/线程变动/句柄变动");
                        break;
                    case 17:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：按键  gd回车或ESC退出");
                        break;
                    case 18:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：CPU(触发) 使用率>=15%才上报");
                        break;
                    case 19:
                        EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 采样：刷新  事件采样2秒/界面刷新100ms");
                        break;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private static async Task DashboardMasterLoop(CancellationToken token)
    {
        try
        {
            long tick = 0;
            List<string> eventLogs = new() { "[启动] 守护界面已加载", "[启动] 监听器已上线，开始采集" };
            var memSeries = new RingSeries(30);
            var thdSeries = new RingSeries(30);
            var diskSeries = new RingSeries(30);

            EnqueueEvent($"[{DateTime.Now:HH:mm:ss}] 守护：模式已启用");

            while (!token.IsCancellationRequested)
            {
                int w = Console.WindowWidth;
                int h = Console.WindowHeight;

                // Sample metrics (stable, meaningful)
                memSeries.Add((int)Math.Clamp(_currentProcess.WorkingSet64 / 1024 / 1024, 0, 9999));
                thdSeries.Add(_currentProcess.Threads.Count);
                diskSeries.Add(GetPrimaryDriveFreePercent());

                // Pull queued events (keep a larger buffer, UI will show latest lines that fit)
                while (_events.TryDequeue(out var ev))
                {
                    eventLogs.Add(ev);
                }

                while (eventLogs.Count > 300) eventLogs.RemoveAt(0);

                // No heartbeat here: event stream is produced by probes (rotating telemetry + triggers)

                RenderGuardianDashboard(w, h, tick, eventLogs, memSeries, thdSeries, diskSeries);

                tick++;
                await Task.Delay(100, token);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private static void RenderGuardianDashboard(int w, int h, long tick, List<string> logs, RingSeries memSeries, RingSeries thdSeries, RingSeries diskSeries)
    {
        if (w < 100 || h < 25) return;

        try
        {
            ConsoleColor faint = ConsoleColor.DarkGray;
            ConsoleColor frame = ConsoleColor.DarkCyan;
            ConsoleColor accent = (tick % 20 < 10) ? ConsoleColor.Cyan : ConsoleColor.White;

            int leftX = 2;
            int topY = 1;
            int fullW = w - 4;

            // Header
            Console.ForegroundColor = frame;
            WriteAt(leftX, topY, new string('-', Math.Min(fullW, w - leftX - 1)));
            Console.ForegroundColor = accent;
            WriteAt(leftX, topY + 1, "守护模式：已启用（输入 gd 回车退出）");
            Console.ForegroundColor = faint;
            WriteAt(leftX + 42, topY + 1, $"主机:{Environment.MachineName}  在线:{TimeSpan.FromMilliseconds(Environment.TickCount64):dd\\:hh\\:mm\\:ss}  时间:{DateTime.Now:HH:mm:ss}");
            WriteAt(leftX + 42, topY + 2, $"总运行时长: {Utils.FormatRuntime(Program.TotalRuntimeSeconds)}");
            Console.ForegroundColor = frame;
            WriteAt(leftX, topY + 2, new string('-', Math.Min(fullW, w - leftX - 1)));

            int panelTop = topY + 3;
            int gap = 2;
            int colW = (fullW - gap * 2) / 3;
            int col1 = leftX;
            int col2 = col1 + colW + gap;
            int col3 = col2 + colW + gap;
            int panelH = 13;

            // Left: 进程监控（加密度：不新增类目，只加更多字段）
            Console.ForegroundColor = frame;
            DrawAsciiPanel(col1, panelTop, colW, panelH, "进程监控");
            Console.ForegroundColor = ConsoleColor.White;
            WriteAt(col1 + 2, panelTop + 2, $"进程名 : {_currentProcess.ProcessName}");
            WriteAt(col1 + 2, panelTop + 3, $"进程ID : {_currentProcess.Id}");
            WriteAt(col1 + 2, panelTop + 4, $"线程数 : {_currentProcess.Threads.Count}");
            WriteAt(col1 + 2, panelTop + 5, $"句柄数 : {_currentProcess.HandleCount}");
            WriteAt(col1 + 2, panelTop + 6, $"优先级 : {_currentProcess.BasePriority}");
            WriteAt(col1 + 2, panelTop + 7, $"占用内存: {Utils.FormatSize(_currentProcess.WorkingSet64)}");
            Console.ForegroundColor = faint;
            WriteAt(col1 + 2, panelTop + 9, $"内存曲线: {memSeries.RenderSparkline(Math.Max(10, colW - 12))}");
            WriteAt(col1 + 2, panelTop + 10, $"线程曲线: {thdSeries.RenderSparkline(Math.Max(10, colW - 12))}");
            WriteAt(col1 + 2, panelTop + 11, $"GC次数 : {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");

            // Mid: 系统状态（中文化 + 增加字段）
            Console.ForegroundColor = frame;
            DrawAsciiPanel(col2, panelTop, colW, panelH, "系统状态");
            Console.ForegroundColor = ConsoleColor.White;
            WriteAt(col2 + 2, panelTop + 2, $"运行时 : .NET {Environment.Version}");
            WriteAt(col2 + 2, panelTop + 3, $"架构   : {Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")}");
            WriteAt(col2 + 2, panelTop + 4, $"核心数 : {Environment.ProcessorCount}");
            WriteAt(col2 + 2, panelTop + 5, $"系统版本: {Environment.OSVersion.Version}");
            WriteAt(col2 + 2, panelTop + 6, $"用户名 : {Environment.UserName}");
            Console.ForegroundColor = faint;
            WriteAt(col2 + 2, panelTop + 10, $"磁盘曲线: {diskSeries.RenderSparkline(Math.Max(10, colW - 12))}");

            var d = GetPrimaryDriveSafe();
            if (d is not null)
            {
                var freePct = (int)Math.Round(GetDriveFreePercent(d));
                Console.ForegroundColor = ConsoleColor.White;
                WriteAt(col2 + 2, panelTop + 8, $"系统盘 : {d.Name.TrimEnd('\\')}  空闲{freePct}%");
                Console.ForegroundColor = faint;
                WriteAt(col2 + 2, panelTop + 9, $"空闲量 : {Utils.FormatSize(d.AvailableFreeSpace)}");
            }

            // Right: 监听器（全中文）
            Console.ForegroundColor = frame;
            DrawAsciiPanel(col3, panelTop, colW, panelH, "监听器");
            Console.ForegroundColor = ConsoleColor.White;
            WriteAt(col3 + 2, panelTop + 2, "进程监听 : 已启用");
            WriteAt(col3 + 2, panelTop + 3, "网络监听 : 已启用");
            WriteAt(col3 + 2, panelTop + 4, "磁盘监听 : 已启用");
            WriteAt(col3 + 2, panelTop + 5, "按键监听 : 已启用");
            WriteAt(col3 + 2, panelTop + 6, "退出命令 : gd + 回车");
            Console.ForegroundColor = faint;
            WriteAt(col3 + 2, panelTop + 8, $"事件队列 : {_events.Count}");
            WriteAt(col3 + 2, panelTop + 9, "刷新周期 : 100ms");

            // Bottom events
            int logsTop = panelTop + panelH + 1;
            int logsH = h - logsTop - 2;
            if (logsH >= 7)
            {
                Console.ForegroundColor = frame;
                DrawAsciiPanel(leftX, logsTop, fullW, logsH, "遥测 / 事件流");

                int lines = Math.Max(1, logsH - 3);
                int start = Math.Max(0, logs.Count - lines);
                int logY = logsTop + 2;
                Console.ForegroundColor = faint;
                for (int i = start; i < logs.Count; i++)
                {
                    if (logY >= logsTop + logsH - 1) break;
                    WriteAt(leftX + 2, logY++, Utils.Truncate(logs[i], fullW - 4));
                }
            }

        }
        catch { }

        Console.ResetColor();
    }

    private static (long TotalRx, long TotalTx) SnapshotNicBytes()
    {
        try
        {
            long rx = 0;
            long tx = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var stats = ni.GetIPv4Statistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }
            return (rx, tx);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static void DrawIndustrialBox(int x, int y, int width, int height, string title)
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.SetCursorPosition(x, y);
        Console.Write("+" + new string('-', width - 2) + "+");
        for (int i = 1; i < height - 1; i++)
        {
            Console.SetCursorPosition(x, y + i);
            Console.Write("|" + new string(' ', width - 2) + "|");
        }
        Console.SetCursorPosition(x, y + height - 1);
        Console.Write("+" + new string('-', width - 2) + "+");

        Console.SetCursorPosition(x + 2, y);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($" {title} ");
    }

    private static void DrawAsciiPanel(int x, int y, int width, int height, string title)
    {
        width = Math.Max(10, width);
        height = Math.Max(5, height);

        Console.SetCursorPosition(x, y);
        Console.Write("+" + new string('-', width - 2) + "+");
        for (int i = 1; i < height - 1; i++)
        {
            Console.SetCursorPosition(x, y + i);
            Console.Write("|" + new string(' ', width - 2) + "|");
        }
        Console.SetCursorPosition(x, y + height - 1);
        Console.Write("+" + new string('-', width - 2) + "+");

        var capped = Utils.Truncate(title, Math.Max(0, width - 6));
        Console.SetCursorPosition(x + 2, y);
        Console.Write($"[ {capped} ]");
    }

    private static void WriteAt(int x, int y, string text)
    {
        if (y < 0 || y >= Console.WindowHeight) return;
        if (x < 0) x = 0;
        if (x >= Console.WindowWidth) return;

        int max = Console.WindowWidth - x;
        if (max <= 0) return;
        if (text.Length > max) text = text[..max];

        Console.SetCursorPosition(x, y);
        Console.Write(text);
    }

    private static DriveInfo? GetPrimaryDriveSafe()
    {
        try
        {
            return DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady);
        }
        catch
        {
            return null;
        }
    }

    private static int GetPrimaryDriveFreePercent()
    {
        var d = GetPrimaryDriveSafe();
        if (d is null || d.TotalSize <= 0) return 0;
        return (int)Math.Round(GetDriveFreePercent(d));
    }

    private static double GetDriveFreePercent(DriveInfo d)
    {
        try
        {
            return (double)d.AvailableFreeSpace / d.TotalSize * 100.0;
        }
        catch
        {
            return 0;
        }
    }

    private sealed class RingSeries
    {
        private readonly int[] _values;
        private int _idx;
        private int _count;

        public RingSeries(int capacity)
        {
            _values = new int[Math.Max(8, capacity)];
        }

        public int Latest => _count == 0 ? 0 : _values[(_idx - 1 + _values.Length) % _values.Length];

        public void Add(int value)
        {
            _values[_idx] = value;
            _idx = (_idx + 1) % _values.Length;
            _count = Math.Min(_count + 1, _values.Length);
        }

        public string RenderSparkline(int width)
        {
            width = Math.Max(8, width);
            if (_count == 0) return new string('.', width);

            int[] tmp = new int[_count];
            for (int i = 0; i < _count; i++)
            {
                int src = (_idx - _count + i + _values.Length) % _values.Length;
                tmp[i] = _values[src];
            }

            int min = tmp.Min();
            int max = tmp.Max();
            int range = Math.Max(1, max - min);

            var sb = new StringBuilder(width);
            for (int i = 0; i < width; i++)
            {
                int src = (int)Math.Round(i * (tmp.Length - 1) / (double)(width - 1));
                int v = tmp[src];
                double norm = (v - min) / (double)range;

                // ASCII "levels" (clean and readable)
                sb.Append(norm switch
                {
                    < 0.20 => '.',
                    < 0.40 => ':',
                    < 0.60 => '-',
                    < 0.80 => '=',
                    _ => '#'
                });
            }
            return sb.ToString();
        }
    }
}