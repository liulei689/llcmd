using System.Text;
using LL;

// ==================================================================================
// LL CLI TOOL - Professional Edition
// ==================================================================================

// 环境配置
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.Unicode;
Console.Title = "LL 命令行工具";
// 初始化并注册
Initialize();

// 内部模式：仅用于按需提权执行单条命令
if (args.Length > 1 && args[0].Equals("--elevated-run", StringComparison.OrdinalIgnoreCase))
{
    CommandManager.ExecuteCommand(args[1], args.Skip(2).ToArray());
    return;
}

// 入口点
if (args.Length == 0)
{
    UI.PrintBanner();
    // 默认开启 2 小时闲时关机监听
    PowerManager.StartIdleMonitor(new[] { "2h" });
    EnterInteractiveMode();
}
else
{
    CommandManager.ExecuteCommand(args[0], args.Skip(1).ToArray());
}

void Initialize()
{
    // 系统指令
    CommandManager.RegisterCommand(1,  "ls",   "指令清单", _ => ShowList());
    CommandManager.RegisterCommand(1,  "list", "指令清单", _ => ShowList());
    CommandManager.RegisterCommand(1,  "help", "查看帮助", _ => ShowList());

    CommandManager.RegisterCommand(2,  "gd",   "守护模式", args => GuardianManager.ToggleGuardianMode(args));
    CommandManager.RegisterCommand(3,  "sd",   "倒数关机", args => PowerManager.StartShutdownSequence(args));
    CommandManager.RegisterCommand(4,  "idle", "空闲关机", args => PowerManager.StartIdleMonitor(args));
    CommandManager.RegisterCommand(5,  "st",   "任务状态", _ => PowerManager.ShowStatus());
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

void ShowList()
{
    UI.PrintHeader("指令列表");
    CommandManager.ShowCommands();
    Console.WriteLine();
    UI.PrintHeader("快捷启动");
    ShortcutManager.ShowShortcuts();
    Console.WriteLine();
}

void EnterInteractiveMode()
{
    UI.PrintSuccess("交互模式已就绪");
    // 提示信息移除：保持界面干净

    HistoryManager.EnsureSessionLoaded();

    while (true)
    {
        // Use ANSI escape sequences for colored prompt. ReadLine will render it if VT is enabled.
        var prompt = $"\u001b[32m{Environment.UserName}\u001b[37m@\u001b[35m{Environment.MachineName}\u001b[33m $\u001b[0m";
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

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        CommandManager.ExecuteCommand(parts[0], parts.Skip(1).ToArray());
    }
}

static string? ReadLineWithEditing(string prompt)
{
    Console.Write(prompt);
    // Calculate visible prompt length (excluding ANSI escape sequences)
    int visiblePromptLen = Environment.UserName.Length + 1 + Environment.MachineName.Length + 2; // user@host $
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
