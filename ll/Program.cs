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
using System.Threading;
using System.IO;
using System.Data;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;

// ==================================================================================
// LL CLI TOOL - Professional Edition
// ==================================================================================

class Program
{
    // 全局默认项目路径（由 cd/proj 命令设置，供其他命令使用）
    internal static string? CurrentProjectPath;

    // 共享 SSH 连接实例（用于数据库和远程命令）
    internal static SSHConn? SharedSSHConn;

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
    internal static long _emailSendCount;
    internal static long _dbStoreCount;
    public static long TotalRuntimeSeconds { get; private set; }
    public static long EmailSendCount { get; private set; }
    public static long DbStoreCount { get; private set; }
    private static DateTime _lastUpdateTime;

    static void Main(string[] args)
    {
        // 环境配置
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.Unicode;
        Console.Title = "LL 命令行工具";
        Initialize(); // 仅注册命令
                // 内部模式：仅用于按需提权执行单条命令
        if (args.Length > 0)
        {
            if(args[0].Equals("--elevated-run", StringComparison.OrdinalIgnoreCase))
            CommandManager.ExecuteCommand(args[1], args.Skip(2).ToArray());
            else
             CommandManager.ExecuteCommand(args[0], args.Skip(1).ToArray());
            return;
        }
        // 交互模式/提权模式：执行完整初始化流程
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
        bool startMinimized = ConfigManager.GetValue("StartMinimized", true);
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

    



        // 初始化热键
        HotkeyManager.Initialize();
        //try
        //{
        //    string monitorExeName = "ClipboardMonitor.exe";
        //    string monitorPath = Path.Combine(AppContext.BaseDirectory, "net10.0-windows", monitorExeName);
        //    // 检查是否已经在运行
        //    var runningProcesses = Process.GetProcessesByName("ClipboardMonitor");
        //    if (runningProcesses.Length == 0)
        //    {
        //        Process.Start(monitorPath);
        //        Console.WriteLine("光标输入监控成功");
        //    }
        //    else
        //    {
        //        Console.WriteLine("光标输入监控成功");
        //    }
        //}
        //catch (Exception ex)
        //{
        //    Console.WriteLine($" {ex.Message}");
        //}

        // 启动自动输入脚本
        try
        {
            string ahkScriptPath = Path.Combine(AppContext.BaseDirectory, "tools", "自动输入.ahk");
            if (File.Exists(ahkScriptPath))
            {
                string ahkExePath = @"C:\Program Files\AutoHotkey\v2\AutoHotkey.exe";
                if (!File.Exists(ahkExePath))
                {
                    ahkExePath = @"C:\Program Files (x86)\AutoHotkey\v2\AutoHotkey.exe";
                }
                if (File.Exists(ahkExePath))
                {
                    var psi = new ProcessStartInfo(ahkExePath, ahkScriptPath)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(psi);
                    Console.WriteLine("自动输入脚本启动成功");
                }
                else
                {
                    Console.WriteLine("AutoHotkey.exe 未找到，请确保已安装 AutoHotkey。");
                }
            }
            else
            {
                Console.WriteLine("自动输入脚本文件不存在");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"自动输入脚本启动失败: {ex.Message}");
        }
        // 初始化输入统计
        InputStats.Initialize();

        // 启动后台任务，每分钟更新总运行时长
        _lastUpdateTime = DateTime.Now;
        // 从runtime.json读取初始总运行时长
        var runtimePath = Path.Combine(AppContext.BaseDirectory, "runtime.json");
        try
        {
            _totalRuntimeSeconds = ConfigManager.GetValue("TotalRuntimeSeconds", 0L, runtimePath);
            TotalRuntimeSeconds = _totalRuntimeSeconds;

            // 读取启动次数和上次启动时间
            long launchCount = ConfigManager.GetValue("LaunchCount", 0L, runtimePath);
            string lastLaunchTimeStr = ConfigManager.GetValue("LastLaunchTime", "", runtimePath);
            DateTime lastLaunchTime = string.IsNullOrEmpty(lastLaunchTimeStr) ? DateTime.MinValue : DateTime.Parse(lastLaunchTimeStr);

            // 读取邮件发送次数和数据库存储次数
            _emailSendCount = ConfigManager.GetValue("EmailSendCount", 0L, runtimePath);
            _dbStoreCount = ConfigManager.GetValue("DbStoreCount", 0L, runtimePath);
            EmailSendCount = _emailSendCount;
            DbStoreCount = _dbStoreCount;

            // 累加启动次数，更新上次启动时间
            launchCount++;
            DateTime now = DateTime.Now;
            ConfigManager.SetValue("LaunchCount", launchCount, runtimePath);
            ConfigManager.SetValue("LastLaunchTime", now.ToString("yyyy-MM-dd HH:mm:ss"), runtimePath);
            TimeSpan totalTime = TimeSpan.FromSeconds(TotalRuntimeSeconds);
            ConfigManager.SetValue("LastLaunchTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), runtimePath);
            UI.PrintInfo($"系统总运行时长: {totalTime.Days}天 {totalTime.Hours}小时 {totalTime.Minutes}分钟 {totalTime.Seconds}秒，启动次数: {launchCount}，上次启动: {lastLaunchTimeStr}，邮件发送次数: {EmailSendCount}，数据库存储次数: {DbStoreCount}，鼠标点击次数: {InputStats.MouseClickCount} ({InputStats.SessionMouseClickCount})，键盘按键次数: {InputStats.KeyboardPressCount} ({InputStats.SessionKeyboardPressCount})");
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
                EmailSendCount = _emailSendCount;
                DbStoreCount = _dbStoreCount;
                UpdateConfigTotalRuntime(_totalRuntimeSeconds);
                InputStats.UpdateStats();
            }
        });
        // 进入交互模式（能走到这里只可能是 args.Length == 0，命令模式已在前面处理）
        UI.PrintBanner();
        // 显示总运行时长
        UpdateConsoleTitle();
        // 默认开启 2 小时时间关机监听
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
            Program._dbStoreCount++;
            IsDBListening = true;
            UpdateConsoleTitle();
        }
        EnterInteractiveMode(supportsVT);

    }

    static void Initialize()
    {
        // delegate to external initializer for easier viewing
        Initializer.Initialize();
    }

    internal static void ShowList()
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
            // Clear input buffer to prevent stuck from previous commands
            while (Console.KeyAvailable) Console.ReadKey(true);

            // Use ANSI escape sequences if VT is supported, otherwise plain text
            string defaultProj = LL.GitCommandHandler.GetCurrentProjectPath();
            string projName = null;
            if (!string.IsNullOrEmpty(defaultProj))
            {
                try { projName = Path.GetFileName(defaultProj.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)); } catch { projName = defaultProj; }
            }
            string prompt = supportsVT
                ? $"\u001b[32m{Environment.UserName}\u001b[37m@\u001b[35m{Environment.MachineName}\u001b[33m {(string.IsNullOrEmpty(projName) ? "$" : $"[{projName}]") }\u001b[0m "
                : $"{Environment.UserName}@{Environment.MachineName} {(string.IsNullOrEmpty(projName) ? "$" : $"[{projName}]")} ";
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
            if (File.Exists(trimmedInput) && trimmedInput.Length < 260) // limit path length
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

            // Check if input is a math expression
            if (Regex.IsMatch(input, @"[\+\-\*/]") && input.Length < 100) // limit length
            {
                try
                {
                    var result = ComputeExpression(input);
                    UI.PrintInfo($"计算结果: {result}");
                    continue;
                }
                catch
                {
                    // not a valid math expression, execute command
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
            string cleanPrompt = Regex.Replace(prompt, "\u001b\\[[0-9;]*m", string.Empty);
            int visiblePromptLen = GetDisplayWidth(cleanPrompt);
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
            string displayText = input.Length <= Console.WindowWidth - visiblePromptLen - 10
                ? input.ToString()
                : "..." + input.ToString().Substring(Math.Max(0, input.Length - (Console.WindowWidth - visiblePromptLen - 13)));
            Console.SetCursorPosition(visiblePromptLen, Console.CursorTop);
            Console.Write(new string(' ', Math.Max(0, Console.WindowWidth - visiblePromptLen)));
            Console.SetCursorPosition(visiblePromptLen, Console.CursorTop);
            Console.Write(displayText);
            // Calculate display width up to cursor for accurate cursor positioning with wide characters
            int displayWidthToCursor = GetDisplayWidth(displayText.AsSpan(0, Math.Min(cursor, displayText.Length)));
            try
            {
                int cursorLeft = visiblePromptLen + displayWidthToCursor;
                if (cursorLeft >= Console.WindowWidth)
                {
                    cursorLeft = Console.WindowWidth - 1;
                }
                Console.SetCursorPosition(cursorLeft, Console.CursorTop);
            }
            catch (ArgumentOutOfRangeException)
            {
                // 如果仍出错，设置到窗口末尾
                Console.SetCursorPosition(Console.WindowWidth - 1, Console.CursorTop);
            }
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

    internal static void ManageAutoStart(string[] args)
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

    internal static void ShowAllStatus()
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

    internal static async void ListenPostgreSQL(string[] args)
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

    internal static void UnlistenPostgreSQL()
    {
        ListenManager.StopListen();
    }

    internal static async void NotifyPostgreSQL(string[] args)
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
                SharedSSHConn = sshConn;
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

    internal static void EncryptCommand(string[] args)
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

    internal static void DecryptCommand(string[] args)
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
        var runtimePath = Path.Combine(AppContext.BaseDirectory, "runtime.json");
        ConfigManager.SetValue("TotalRuntimeSeconds", totalSeconds, runtimePath);
        ConfigManager.SetValue("EmailSendCount", _emailSendCount, runtimePath);
        ConfigManager.SetValue("DbStoreCount", _dbStoreCount, runtimePath);
    }

   public static void ShowHome()
    {
        UI.PrintBanner();
        // 显示总运行时长
        TimeSpan totalTime = TimeSpan.FromSeconds(TotalRuntimeSeconds);
        var runtimePath = Path.Combine(AppContext.BaseDirectory, "runtime.json");
        long launchCount = ConfigManager.GetValue("LaunchCount", 0L, runtimePath);
        string lastLaunchTimeStr = ConfigManager.GetValue("LastLaunchTime", "", runtimePath);
        UI.PrintInfo($"系统总运行时长: {totalTime.Days}天 {totalTime.Hours}小时 {totalTime.Minutes}分钟 {totalTime.Seconds}秒，启动次数: {launchCount}，上次启动: {lastLaunchTimeStr}，邮件发送次数: {EmailSendCount}，数据库存储次数: {DbStoreCount}，鼠标点击次数: {InputStats.MouseClickCount} ({InputStats.SessionMouseClickCount})，键盘按键次数: {InputStats.KeyboardPressCount} ({InputStats.SessionKeyboardPressCount})");
    }

    [UnconditionalSuppressMessage("IL", "IL2026")]
    private static object ComputeExpression(string expression)
    {
        return new DataTable().Compute(expression, null);
    }
}

