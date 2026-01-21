using System.Text;
using LL;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Diagnostics;
using Npgsql;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using LL.Native;

// ==================================================================================
// LL CLI TOOL - Professional Edition
// ==================================================================================

class Program
{
    // P/Invoke declarations for window manipulation
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_STYLE = -16;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_SYSMENU = 0x00080000;

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    public static DateTime ProgramStartTime = DateTime.Now;

    public static bool HasDb = false;
    public static bool IsIdleMonitoring = false;
    public static bool IsSSHConnected = false;
    public static bool IsDBListening = false;
    public static string IdleTimeDisplay = "";
    public static string ShutdownTimeDisplay = "";
    public static DateTime? GuardianStartTime;
    public static DateTime? LockStartTime;
    public static int GuardianCountdown = 0;
    public static int LockCountdown = 0;
    private static long _totalRuntimeSeconds;
    public static long TotalRuntimeSeconds { get; private set; }
    private static DateTime _lastUpdateTime;

    static void Main(string[] args)
    {
        // 环境配置
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.Unicode;
        Console.Title = "LL 命令行工具";
        // 顶部控制栏设为黑色（Win10+，仅对控制台窗口有效）
        IntPtr hWnd = LL.Native.NativeMethods.GetConsoleWindow();
        if (hWnd != IntPtr.Zero)
        {
            // DWM API: Immersive dark mode for title bar
            const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
            int dark = 1;
            DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }
        // Hide maximize, minimize, and close buttons
        HideConsoleButtons();

        // Check for start minimized
        bool startMinimized = true; // default
        var configPathMin = Path.Combine(AppContext.BaseDirectory, "config.json");
        if (File.Exists(configPathMin))
        {
            var configJson = File.ReadAllText(configPathMin);
            var configDoc = System.Text.Json.JsonDocument.Parse(configJson);
            if (configDoc.RootElement.TryGetProperty("StartMinimized", out var minProp))
            {
                startMinimized = minProp.GetBoolean();
            }
        }
        if (startMinimized)
        {
            IntPtr hWndMin = LL.Native.NativeMethods.GetConsoleWindow();
            if (hWndMin != IntPtr.Zero)
            {
                LL.Native.NativeMethods.ShowWindow(hWndMin, LL.Native.NativeMethods.SW_MINIMIZE);
            }
        }

        // Check for VT support
        bool supportsVT = EnableVirtualTerminalProcessing();

        // 初始化并注册
        Initialize();
        // 内部模式：仅用于按需提权执行单条命令
        if (args.Length > 1 && args[0].Equals("--elevated-run", StringComparison.OrdinalIgnoreCase))
        {
            CommandManager.ExecuteCommand(args[1], args.Skip(2).ToArray());
            return;
        }
        // 启动后台任务，每分钟更新总运行时长
        _lastUpdateTime = DateTime.Now;
        // 从config读取初始总运行时长
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var node = JsonNode.Parse(json);
                if (node is JsonObject obj && obj["TotalRuntimeSeconds"] is JsonValue val && val.TryGetValue(out long total))
                {
                    _totalRuntimeSeconds = total;
                    TotalRuntimeSeconds = total;
                }
            }
        }
        catch { }
        Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(1));
                var now = DateTime.Now;
                _totalRuntimeSeconds += (long)(now - _lastUpdateTime).TotalSeconds;
                _lastUpdateTime = now;
                TotalRuntimeSeconds = _totalRuntimeSeconds;
                UpdateConfigTotalRuntime(_totalRuntimeSeconds);
            }
        });
        // 入口点
        if (args.Length == 0)
        {
            UI.PrintBanner();
            // 显示总运行时长
            TimeSpan totalTime = TimeSpan.FromSeconds(TotalRuntimeSeconds);
            UI.PrintInfo($"系统总运行时长: {totalTime.Days}天 {totalTime.Hours}小时 {totalTime.Minutes}分钟 {totalTime.Seconds}秒");
            UpdateConsoleTitle();
            // 默认开启 2 小时闲时关机监听
            PowerManager.StartIdleMonitor(new[] { "2h" });
            IsIdleMonitoring = true;
            UpdateConsoleTitle();
            // 默认启动数据库监听
            string connString = GetConnectionString();
            HasDb = connString != null;
            if (HasDb)
            {
                IsSSHConnected = true;
                UpdateConsoleTitle();
                LogManager.Initialize(connString);
                ListenManager.StartListen("ll_notifications", connString);
                LogManager.Log("Info", "System", "CLI 启动");
                IsDBListening = true;
                UpdateConsoleTitle();
            }
            EnterInteractiveMode(supportsVT);
        }
        else
        {
            CommandManager.ExecuteCommand(args[0], args.Skip(1).ToArray());
        }

    }

    static void Initialize()
    {
        // 系统指令
        CommandManager.RegisterCommand(1,  "ls",   "指令清单", _ => ShowList());
        CommandManager.RegisterCommand(1,  "list", "指令清单", _ => ShowList());
        CommandManager.RegisterCommand(1,  "help", "查看帮助", _ => ShowList());

        CommandManager.RegisterCommand(2,  "gd",   "守护模式", args => GuardianManager.ToggleGuardianMode(args));
        CommandManager.RegisterCommand(3,  "sd",   "倒数关机", args => PowerManager.StartShutdownSequence(args));
        CommandManager.RegisterCommand(4,  "idle", "空闲关机", args => PowerManager.StartIdleMonitor(args));
        CommandManager.RegisterCommand(5,  "st",   "任务状态", _ => ShowAllStatus());
        CommandManager.RegisterCommand(6,  "c",    "取消任务", _ => TaskManager.CancelLatest());
        CommandManager.RegisterCommand(7,  "abort","中止关机", _ => PowerManager.AbortSystemShutdown());

        CommandManager.RegisterCommand(8,  "myip", "查看本机 IP", _ => QuickCommands.ShowMyIp());
        CommandManager.RegisterCommand(9,  "netspeed", "测当前网速", args => NetSpeed.Measure(args));
        CommandManager.RegisterCommand(10, "lan",  "扫描局域网设备", args => LanScanner.Scan(args));
        CommandManager.RegisterCommand(11, "ip",   "网络信息", _ => UtilityCommands.Execute(["ip"]));

        CommandManager.RegisterCommand(12, "open", "启动程序", args => ShortcutManager.OpenProgram(args));
        CommandManager.RegisterCommand(13, "sys",  "系统信息", args => SystemCommands.ShowSysInfo(args));
        CommandManager.RegisterCommand(14, "time", "系统时间", args => SystemCommands.ShowTime(args));
        CommandManager.RegisterCommand(15, "size", "目录大小", args => SystemCommands.CheckDirectorySize(args));
        CommandManager.RegisterCommand(16, "clr",  "清屏", _ => Console.Clear());
        CommandManager.RegisterCommand(17, "qr",   "生成二维码", args => QrCommands.Print(args));
        CommandManager.RegisterCommand(18, "admin", "申请管理员权限", args => ElevationCommands.Elevate(args));
        CommandManager.RegisterCommand(19, "settime", "修改系统时间", args => SystemCommands.SetTime(args));
        CommandManager.RegisterCommand(99, "exit", "退出", _ => Environment.Exit(0));

        CommandManager.RegisterCommand(100, "min", "最小化窗口", _ => { IntPtr hWnd = NativeMethods.GetConsoleWindow(); NativeMethods.ShowWindow(hWnd, NativeMethods.SW_MINIMIZE); });

        // 实用工具（扁平化：直接用命令名，不用 util 前缀）
        CommandManager.RegisterCommand(20, "ps", "进程列表", args => UtilityCommands.Execute(["ps", ..args]));
        CommandManager.RegisterCommand(21, "kill", "结束进程", args => UtilityCommands.Execute(["kill", ..args]));
        CommandManager.RegisterCommand(22, "port", "端口检测", args => UtilityCommands.Execute(["port", ..args]));
        // ip 已在上面手动编号
        CommandManager.RegisterCommand(23, "curl", "HTTP GET", args => UtilityCommands.Execute(["curl", ..args]));
        CommandManager.RegisterCommand(24, "dns", "DNS 解析", args => UtilityCommands.Execute(["dns", ..args]));
        CommandManager.RegisterCommand(25, "find", "查找文件", args => UtilityCommands.Execute(["find", ..args]));
        CommandManager.RegisterCommand(26, "watch", "监听目录", args => UtilityCommands.Execute(["watch", ..args]));
        CommandManager.RegisterCommand(27, "clip", "剪贴板", args => UtilityCommands.Execute(["clip", ..args]));
        CommandManager.RegisterCommand(28, "path", "PATH", args => UtilityCommands.Execute(["path", ..args]));
        CommandManager.RegisterCommand(29, "env", "环境变量", args => UtilityCommands.Execute(["env", ..args]));
        CommandManager.RegisterCommand(30, "clean", "清理", args => UtilityCommands.Execute(["clean", ..args]));
        CommandManager.RegisterCommand(31, "hist", "历史记录", args => HistoryCommands.Show(args));

        CommandManager.RegisterCommand(32, "loc", "统计目录代码行数", args => CodeStatsCommands.Run(args));

        CommandManager.RegisterCommand(60, "encv", "加密视频文件(生成 .llv)", args => VideoVaultCommands.Encrypt(args));
        CommandManager.RegisterCommand(61, "playv", "播放加密视频(.llv)", args => VideoVaultCommands.Play(args));
        CommandManager.RegisterCommand(62, "lsv", "列出加密视频(.llv)", args => VideoVaultCommands.List(args));
        CommandManager.RegisterCommand(63, "clrv", "清理视频临时解密文件", args => VideoVaultCommands.CleanTemp(args));
        CommandManager.RegisterCommand(64, "decv", "解密视频文件(.llv 到 .mp4)", args => VideoVaultCommands.Decrypt(args));
        CommandManager.RegisterCommand(65, "encf", "加密文件(生成 .llf)", args => FileVaultCommands.EncryptFile(args));
        CommandManager.RegisterCommand(66, "decf", "解密文件(.llf 到原格式)", args => FileVaultCommands.DecryptFile(args));

        // 开机启动管理
        CommandManager.RegisterCommand(70, "autostart", "开机启动管理", args => ManageAutoStart(args));

        // PostgreSQL 监听通知
        CommandManager.RegisterCommand(71, "listen", "监听PostgreSQL通知", args => ListenPostgreSQL(args));
        CommandManager.RegisterCommand(72, "notify", "发送PostgreSQL通知", args => NotifyPostgreSQL(args));
        CommandManager.RegisterCommand(73, "unlisten", "停止监听PostgreSQL通知", _ => UnlistenPostgreSQL());

        CommandManager.RegisterCommand(80, "encrypt", "加密", args => EncryptCommand(args));
        CommandManager.RegisterCommand(81, "decrypt", "解密", args => DecryptCommand(args));
        // 常用快捷操作（面向普通用户）
        CommandManager.RegisterCommand(40, "task", "任务管理器", _ => QuickCommands.OpenTaskManager());
        CommandManager.RegisterCommand(41, "dev", "设备管理器", _ => QuickCommands.OpenDeviceManager());
        CommandManager.RegisterCommand(42, "ctrl", "控制面板", _ => QuickCommands.OpenControlPanel());
        CommandManager.RegisterCommand(43, "set", "系统设置", _ => QuickCommands.OpenSettings());
        CommandManager.RegisterCommand(44, "netset", "网络设置", _ => QuickCommands.OpenNetworkSettings());
        CommandManager.RegisterCommand(45, "sound", "声音设置", _ => QuickCommands.OpenSoundSettings());
        CommandManager.RegisterCommand(46, "disp", "显示设置", _ => QuickCommands.OpenDisplaySettings());
        CommandManager.RegisterCommand(47, "store", "存储管理", _ => QuickCommands.OpenStorageSettings());
        CommandManager.RegisterCommand(48, "desk", "打开桌面", _ => QuickCommands.OpenDesktopFolder());
        CommandManager.RegisterCommand(49, "tmp", "打开临时目录", _ => QuickCommands.OpenTempFolder());
        CommandManager.RegisterCommand(50, "recycle", "回收站", _ => QuickCommands.OpenRecycleBin());
        CommandManager.RegisterCommand(51, "snip", "截图工具", _ => QuickCommands.OpenSnippingTool());
        CommandManager.RegisterCommand(52, "dnsflush", "清 DNS 缓存", _ => QuickCommands.FlushDns());
        CommandManager.RegisterCommand(53, "netfix", "网络修复", _ => QuickCommands.NetFix());
        // lan/myip/netspeed 已在上面手动编号

        // 快捷启动程序注册
        ShortcutManager.RegisterShortcut(101, "vs",     "Visual Studio", "devenv");
        ShortcutManager.RegisterShortcut(102, "vs22",   "Visual Studio 2022", @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe");
        ShortcutManager.RegisterShortcut(103, "vs26",   "Visual Studio 2026", "devenv");
        ShortcutManager.RegisterShortcut(104, "code",   "Visual Studio Code", "code");
        ShortcutManager.RegisterShortcut(105, "cmd",    "Command Prompt", "cmd");
        ShortcutManager.RegisterShortcut(106, "calc",   "计算器", "calc");
        ShortcutManager.RegisterShortcut(107, "notepad","记事本", "notepad");
        ShortcutManager.RegisterShortcut(108, "edge",   "Microsoft Edge", "msedge");
        
        ShortcutManager.RegisterShortcut(121, "cat",    "SakuraCat", @"C:\Program Files\SakuraCat\SakuraCat.exe");
        ShortcutManager.RegisterShortcut(122, "unity",  "Unity Hub", @"D:\unityhuben\Unity Hub\Unity Hub.exe");
        ShortcutManager.RegisterShortcut(123, "music",  "NetEase Music", @"D:\LenovoSoftstore\Install\wangyiyunyinyue\cloudmusic.exe");
        ShortcutManager.RegisterShortcut(124, "word",   "Microsoft Word", @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE");
        ShortcutManager.RegisterShortcut(125, "excel",  "Microsoft Excel", @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE");
        ShortcutManager.RegisterShortcut(126, "remote", "Sunlogin", @"C:\Program Files\Oray\SunLogin\SunloginClient\SunloginClient.exe");
        ShortcutManager.RegisterShortcut(127, "sscom",  "SSCOM Serial", @"C:\Users\liu\OneDrive\Desktop\新建文件夹 (2)\sscom5.13.1.exe");
        ShortcutManager.RegisterShortcut(128, "jmeter", "Apache JMeter", @"C:\Users\liu\OneDrive\Desktop\apache-jmeter-5.6.2\bin\jmeter.bat");
        ShortcutManager.RegisterShortcut(129, "nes",    "小霸王游戏机", @"C:\Users\liu\Downloads\xbwmnq204\小霸王游戏机327合1\smynesc.exe");

        // 快捷启动使用纯数字(101-129) 或 open + 别名
    }

    static void ShowList()
    {
        UI.PrintHeader("指令列表");
        CommandManager.ShowCommands();
        Console.WriteLine();
        UI.PrintHeader("快捷启动");
        ShortcutManager.ShowShortcuts();
        Console.WriteLine();
    }

    static void EnterInteractiveMode(bool supportsVT)
    {
        // UI.PrintSuccess("交互模式已就绪");
        // 提示信息移除：保持界面干净

        HistoryManager.EnsureSessionLoaded();

        while (true)
        {
            // Use ANSI escape sequences if VT is supported, otherwise plain text
            string prompt = supportsVT
                ? $"\u001b[32m{Environment.UserName}\u001b[37m@\u001b[35m{Environment.MachineName}\u001b[33m $\u001b[0m"
                : $"{Environment.UserName}@{Environment.MachineName} $ ";
            string? input;
            try
            {
                input = Console.IsInputRedirected ? Console.ReadLine() : ReadLineWithEditing(prompt);
            }
            catch (IOException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(input)) continue;

            HistoryManager.Add(input);

            // Auto-play .llv files or decrypt .llf files dragged into the console
            var trimmedInput = input.Trim('"');
            if (File.Exists(trimmedInput))
            {
                var ext = Path.GetExtension(trimmedInput);
                if (ext.Equals(".llv", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Write("选择播放方式: 1. 本地播放器 (默认)  2. H5浏览器播放: ");
                    var choice = Console.ReadLine();
                    if (choice == "2")
                    {
                        CommandManager.ExecuteCommand("playv", new[] { trimmedInput, "--html5" });
                    }
                    else
                    {
                        CommandManager.ExecuteCommand("playv", new[] { trimmedInput });
                    }
                    continue;
                }
                else if (ext.Equals(".llf", StringComparison.OrdinalIgnoreCase))
                {
                    CommandManager.ExecuteCommand("decf", new[] { trimmedInput });
                    continue;
                }
            }

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            CommandManager.ExecuteCommand(parts[0], parts.Skip(1).ToArray());
        }
    }

    static string? ReadLineWithEditing(string prompt)
    {
        Console.Write(prompt);
        // Calculate visible prompt length (excluding ANSI escape sequences)
        int visiblePromptLen = prompt.Contains('\u001b') ? Environment.UserName.Length + 1 + Environment.MachineName.Length + 2 : prompt.Length; // user@host $
        var input = new StringBuilder();
        int cursor = 0;
        string? currentHistory = null;
        bool historyMode = false;

        while (true)
        {
            var key = Console.ReadKey(true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return input.ToString();

                case ConsoleKey.LeftArrow:
                    if (cursor > 0) cursor--;
                    break;

                case ConsoleKey.RightArrow:
                    if (cursor < input.Length) cursor++;
                    break;

                case ConsoleKey.Backspace:
                    if (cursor > 0)
                    {
                        input.Remove(cursor - 1, 1);
                        cursor--;
                    }
                    break;

                case ConsoleKey.Delete:
                    if (cursor < input.Length)
                    {
                        input.Remove(cursor, 1);
                    }
                    break;

                case ConsoleKey.UpArrow:
                    currentHistory = HistoryManager.Prev();
                    if (currentHistory != null)
                    {
                        input.Clear();
                        input.Append(currentHistory);
                        cursor = input.Length;
                        historyMode = true;
                    }
                    break;

                case ConsoleKey.DownArrow:
                    currentHistory = HistoryManager.Next();
                    if (currentHistory != null)
                    {
                        input.Clear();
                        input.Append(currentHistory);
                        cursor = input.Length;
                        historyMode = true;
                    }
                    else if (historyMode)
                    {
                        input.Clear();
                        cursor = 0;
                        historyMode = false;
                    }
                    break;

                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        input.Insert(cursor, key.KeyChar);
                        cursor++;
                        historyMode = false; // reset history on edit
                    }
                    break;
            }

            // Redraw the line
            Console.SetCursorPosition(visiblePromptLen, Console.CursorTop);
            Console.Write(new string(' ', Math.Max(0, Console.WindowWidth - visiblePromptLen)));
            Console.SetCursorPosition(visiblePromptLen, Console.CursorTop);
            Console.Write(input.ToString());
            // Calculate display width up to cursor for accurate cursor positioning with wide characters
            int displayWidthToCursor = GetDisplayWidth(input.ToString().AsSpan(0, cursor));
            Console.SetCursorPosition(visiblePromptLen + displayWidthToCursor, Console.CursorTop);
        }
    }

    static int GetDisplayWidth(ReadOnlySpan<char> s)
    {
        int width = 0;
        foreach (char c in s)
        {
            // Approximate: ASCII characters are 1 width, others (e.g., Chinese) are 2
            width += c <= 127 ? 1 : 2;
        }
        return width;
    }

    static void HideConsoleButtons()
    {
        IntPtr hWnd = GetConsoleWindow();
        if (hWnd != IntPtr.Zero)
        {
            int style = GetWindowLong(hWnd, GWL_STYLE);
            // Remove maximize, minimize, and system menu (close button)
            style &= ~(WS_MAXIMIZEBOX | WS_MINIMIZEBOX | WS_SYSMENU);
            SetWindowLong(hWnd, GWL_STYLE, style);
        }
    }

    static bool EnableVirtualTerminalProcessing()
    {
        IntPtr hOut = GetStdHandle(STD_OUTPUT_HANDLE);
        if (hOut != IntPtr.Zero && GetConsoleMode(hOut, out uint mode))
        {
            mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
            if (SetConsoleMode(hOut, mode))
            {
                return true;
            }
        }
        return false;
    }

    static void ManageAutoStart(string[] args)
    {
        if (args.Length == 0)
        {
            // 显示当前状态
            bool isInStartup = IsInStartup();
            UI.PrintInfo($"开机启动状态: {(isInStartup ? "已启用" : "未启用")}");
            return;
        }

        string action = args[0].ToLower();
        if (action == "add" || action == "enable")
        {
            AddToStartup();
        }
        else if (action == "remove" || action == "disable")
        {
            RemoveFromStartup();
        }
        else
        {
            UI.PrintError("用法: autostart [add|remove]");
        }
    }

    static bool IsInStartup()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
        {
            return key?.GetValue("LL_CLI") != null;
        }
    }

    static void AddToStartup()
    {
        string exePath = Process.GetCurrentProcess().MainModule.FileName;
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
        {
            key.SetValue("LL_CLI", exePath);
        }
        UI.PrintSuccess("已添加到开机启动");
    }

    static void RemoveFromStartup()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
        {
            key.DeleteValue("LL_CLI", false);
        }
        UI.PrintSuccess("已从开机启动移除");
    }

    static void ShowAllStatus()
    {
        PowerManager.ShowStatus();
        Console.WriteLine();
        UI.PrintHeader("监听状态");
        if (ListenManager.IsListening)
        {
            UI.PrintResult("状态", "运行中");
            UI.PrintResult("频道", ListenManager.CurrentChannel);
        }
        else
        {
            UI.PrintResult("状态", "未运行");
        }
    }

    static async void ListenPostgreSQL(string[] args)
    {
        string channel = args.Length > 0 ? args[0] : "ll_notifications";
        if (!IsValidChannelName(channel))
        {
            UI.PrintError("频道名无效，只能包含字母、数字、下划线，且不能以数字开头。");
            return;
        }

        string connString = GetConnectionString();
        if (connString == null) return;

        ListenManager.StartListen(channel, connString);
    }

    static void UnlistenPostgreSQL()
    {
        ListenManager.StopListen();
    }

    static async void NotifyPostgreSQL(string[] args)
    {
        if (args.Length == 0)
        {
            UI.PrintError("用法: notify <channel> <message> 或 notify <message> (使用默认频道 'll_notifications')");
            return;
        }

        string channel;
        string message;
        if (args.Length == 1)
        {
            channel = "ll_notifications";
            message = args[0];
        }
        else
        {
            channel = args[0];
            message = string.Join(" ", args.Skip(1));
        }

        if (!IsValidChannelName(channel))
        {
            UI.PrintError("频道名无效，只能包含字母、数字、下划线，且不能以数字开头。");
            return;
        }

        string connString = GetConnectionString();
        if (connString == null) return;

        try
        {
            using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            // Escape single quotes in channel and message
            string escapedChannel = channel.Replace("'", "''");
            string escapedMessage = message.Replace("'", "''");
            using var cmd = new NpgsqlCommand($"SELECT pg_notify('{escapedChannel}', '{escapedMessage}')", conn);
            await cmd.ExecuteNonQueryAsync();

            UI.PrintSuccess($"已发送通知到频道 '{channel}': {message}");
        }
        catch (Exception ex)
        {
            UI.PrintError($"发送失败: {ex.Message}");
        }
    }

    static bool IsValidChannelName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (!char.IsLetter(name[0]) && name[0] != '_') return false;
        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_') return false;
        }
        return true;
    }

    static string GetConnectionString()
    {
        try
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("config.json", optional: true, reloadOnChange: true);
            var config = builder.Build();

            string dbUser = config["Database:Username"] ?? "postgres";
            string dbPass = config["Database:Password"] ?? "";
            string dbName = config["Database:Database"] ?? "wuliudb_new";

            bool sshEnabled = bool.Parse(config["SSH:Enabled"] ?? "false");
            if (sshEnabled)
            {
                var sshConn = new SSHConn();
                if (!sshConn.OpenDbPort())
                {
                    UI.PrintError("SSH隧道建立失败，无法连接数据库。");
                    return null;
                }
                // 使用本地端口
                return $"Host=127.0.0.1;Port={sshConn.LocalPort};Username={dbUser};Password={dbPass};Database={dbName};SSL Mode=Disable;Trust Server Certificate=true;";
            }
            else
            {
                // UI.PrintError("SSH未启用，无法连接数据库。");
                return null;
            }
        }
        catch
        {
            return null;
        }
    }

    static void EncryptCommand(string[] args)
    {
        if (args.Length < 2)
        {
            UI.PrintError("用法: encrypt --string <plaintext> 或 encrypt --db <newpassword> 或 encrypt --all <newpassword>");
            return;
        }

        string option = args[0];
        if (option == "--string")
        {
            string input = string.Join(" ", args.Skip(1)).Trim('"');
            string encrypted = SM4Helper.Encrypt(input);
            UI.PrintInfo($"加密后的密文: {encrypted}");
        }
        else if (option == "--db")
        {
            string newPassword = string.Join(" ", args.Skip(1)).Trim('"');
            string encrypted = SM4Helper.Encrypt(newPassword);
            UpdatePassword("Database:Password", encrypted);
            UI.PrintSuccess("已更新并加密数据库密码。");
        }
        else if (option == "--ssh")
        {
            string newPassword = string.Join(" ", args.Skip(1)).Trim('"');
            string encrypted = SM4Helper.Encrypt(newPassword);
            UpdatePassword("SSH:Password", encrypted);
            UI.PrintSuccess("已更新并加密SSH密码。");
        }
        else if (option == "--email")
        {
            string newPassword = string.Join(" ", args.Skip(1)).Trim('"');
            string encrypted = SM4Helper.Encrypt(newPassword);
            UpdatePassword("Email:Password", encrypted);
            UI.PrintSuccess("已更新并加密邮箱密码。");
        }
        else if (option == "--all")
        {
            string newPassword = string.Join(" ", args.Skip(1)).Trim('"');
            string encrypted = SM4Helper.Encrypt(newPassword);
            UpdatePassword("Database:Password", encrypted);
            UpdatePassword("SSH:Password", encrypted);
            UpdatePassword("Email:Password", encrypted);
            UI.PrintSuccess("已更新并加密所有密码。");
        }
        else
        {
            UI.PrintError("无效选项: --string, --db, --ssh, --email, --all");
        }
    }

    static void UpdatePassword(string keyPath, string newValue)
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var json = File.ReadAllText(configPath);
        var newJson = UpdateJsonValue(json, keyPath, newValue);
        File.WriteAllText(configPath, newJson);
    }

    static string UpdateJsonValue(string json, string keyPath, string newValue)
    {
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);

        // 简单实现，假设结构
        var keys = keyPath.Split(':');
        if (keys.Length == 2)
        {
            if (dict.ContainsKey(keys[0]) && dict[keys[0]] is Dictionary<string, object> sub)
            {
                sub[keys[1]] = newValue;
            }
        }

        return System.Text.Json.JsonSerializer.Serialize(dict, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    static void DecryptCommand(string[] args)
    {
        if (args.Length < 1)
        {
            UI.PrintError("用法: decrypt --string <ciphertext> 或 decrypt --db 或 decrypt --ssh 或 decrypt --email");
            return;
        }

        string option = args[0];
        if (option == "--string")
        {
            if (args.Length < 2)
            {
                UI.PrintError("用法: decrypt --string <ciphertext>");
                return;
            }
            string input = string.Join(" ", args.Skip(1)).Trim('"');
            try
            {
                string decrypted = SM4Helper.Decrypt(input);
                UI.PrintInfo($"解密后的明文: {decrypted}");
            }
            catch
            {
                UI.PrintError("解密失败，可能是密文无效或密钥错误。");
            }
        }
        else if (option == "--db")
        {
            string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (!File.Exists(configPath))
            {
                UI.PrintError("config.json 文件不存在。");
                return;
            }

            var json = File.ReadAllText(configPath);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Database", out var db) && db.TryGetProperty("Password", out var pass))
            {
                string encrypted = pass.GetString();
                try
                {
                    string decrypted = SM4Helper.Decrypt(encrypted);
                    UI.PrintInfo($"数据库密码: {decrypted}");
                }
                catch
                {
                    UI.PrintError("解密失败，可能是密码未加密或密钥错误。");
                }
            }
            else
            {
                UI.PrintError("数据库密码字段不存在。");
            }
        }
        else if (option == "--ssh")
        {
            string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (!File.Exists(configPath))
            {
                UI.PrintError("config.json 文件不存在。");
                return;
            }

            var json = File.ReadAllText(configPath);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("SSH", out var ssh) && ssh.TryGetProperty("Password", out var pass))
            {
                string encrypted = pass.GetString();
                try
                {
                    string decrypted = SM4Helper.Decrypt(encrypted);
                    UI.PrintInfo($"SSH密码: {decrypted}");
                }
                catch
                {
                    UI.PrintError("解密失败，可能是密码未加密或密钥错误。");
                }
            }
            else
            {
                UI.PrintError("SSH密码字段不存在。");
            }
        }
        else if (option == "--email")
        {
            string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (!File.Exists(configPath))
            {
                UI.PrintError("config.json 文件不存在。");
                return;
            }

            var json = File.ReadAllText(configPath);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Email", out var email) && email.TryGetProperty("Password", out var pass))
            {
                string encrypted = pass.GetString();
                try
                {
                    string decrypted = SM4Helper.Decrypt(encrypted);
                    UI.PrintInfo($"邮箱密码: {decrypted}");
                }
                catch
                {
                    UI.PrintError("解密失败，可能是密码未加密或密钥错误。");
                }
            }
            else
            {
                UI.PrintError("邮箱密码字段不存在。”");
            }
        }
        else
        {
            UI.PrintError("无效选项: --string, --db, --ssh, --email");
        }
    }

    public static void UpdateConsoleTitle()
    {
        string idleStatus = IsIdleMonitoring ? "空闲:运行" : "空闲:未";
        string sshStatus = IsSSHConnected ? "SSH:连" : "SSH:未";
        string dbStatus = IsDBListening ? "队列:听" : "队列:未";
        string timeDisplay = "";
        if (GuardianCountdown > 0)
            timeDisplay += $"守护倒计时:{GuardianCountdown / 3600:D2}:{(GuardianCountdown % 3600) / 60:D2}:{GuardianCountdown % 60:D2} ";
        if (LockCountdown > 0)
            timeDisplay += $"锁屏倒计时:{LockCountdown / 3600:D2}:{(LockCountdown % 3600) / 60:D2}:{LockCountdown % 60:D2} ";
        if (!string.IsNullOrEmpty(IdleTimeDisplay))
            timeDisplay += $"{IdleTimeDisplay} ";
        if (!string.IsNullOrEmpty(ShutdownTimeDisplay))
            timeDisplay += $"{ShutdownTimeDisplay} ";
        timeDisplay = timeDisplay.Trim();
        Console.Title = $"LL命令行 | {idleStatus} | {sshStatus} | {dbStatus} | {timeDisplay}";
    }

    private static void UpdateConfigTotalRuntime(long totalSeconds)
    {
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            var json = File.ReadAllText(configPath);
            var node = JsonNode.Parse(json);
            if (node is JsonObject obj)
            {
                obj["TotalRuntimeSeconds"] = totalSeconds;
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                var newJson = node.ToJsonString(options);
                File.WriteAllText(configPath, newJson);
            }
        }
        catch (Exception ex)
        {
            // 可选：记录错误
            Console.WriteLine($"更新配置错误: {ex.Message}");
        }
    }
}

