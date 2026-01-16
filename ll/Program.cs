using System.Text;
using LL;

// ==================================================================================
// LL CLI TOOL - Professional Edition
// ==================================================================================

// 环境配置
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
Console.Title = "LL 命令行工具";

// 初始化并注册
Initialize();

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
    CommandManager.RegisterCommand("help", "查看帮助", _ => ShowList());
    CommandManager.RegisterCommand("list", "指令清单", _ => ShowList());
    CommandManager.RegisterCommand("o",    "启动程序", args => ShortcutManager.OpenProgram(args));
    CommandManager.RegisterCommand("sd",   "倒数关机", args => PowerManager.StartShutdownSequence(args));
    CommandManager.RegisterCommand("idle", "空闲关机", args => PowerManager.StartIdleMonitor(args));
    CommandManager.RegisterCommand("st",   "任务状态", _ => PowerManager.ShowStatus());
    CommandManager.RegisterCommand("c",    "取消任务", _ => PowerManager.CancelTask());
    CommandManager.RegisterCommand("abort","中止关机", _ => PowerManager.AbortSystemShutdown());
    CommandManager.RegisterCommand("size", "目录大小", args => SystemCommands.CheckDirectorySize(args));
    CommandManager.RegisterCommand("time", "系统时间", args => SystemCommands.ShowTime(args));
    CommandManager.RegisterCommand("sys",  "系统信息", args => SystemCommands.ShowSysInfo(args));
    CommandManager.RegisterCommand("gd",   "守护模式", args => GuardianManager.ToggleGuardianMode(args));
    CommandManager.RegisterCommand("clr",  "清屏", _ => Console.Clear());
    CommandManager.RegisterCommand("exit", "退出", _ => Environment.Exit(0));

    // 快捷启动程序注册
    ShortcutManager.RegisterShortcut("vs",     "Visual Studio", "devenv");
    ShortcutManager.RegisterShortcut("vs22",   "Visual Studio 2022", @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe");
    ShortcutManager.RegisterShortcut("vs26",   "Visual Studio 2026", "devenv");
    ShortcutManager.RegisterShortcut("code",   "Visual Studio Code", "code");
    ShortcutManager.RegisterShortcut("cmd",    "Command Prompt", "cmd");
    ShortcutManager.RegisterShortcut("calc",   "计算器", "calc");
    ShortcutManager.RegisterShortcut("notepad","记事本", "notepad");
    ShortcutManager.RegisterShortcut("edge",   "Microsoft Edge", "msedge");
    
    ShortcutManager.RegisterShortcut("cat",    "SakuraCat", @"C:\Program Files\SakuraCat\SakuraCat.exe");
    ShortcutManager.RegisterShortcut("unity",  "Unity Hub", @"D:\unityhuben\Unity Hub\Unity Hub.exe");
    ShortcutManager.RegisterShortcut("music",  "NetEase Music", @"D:\LenovoSoftstore\Install\wangyiyunyinyue\cloudmusic.exe");
    ShortcutManager.RegisterShortcut("word",   "Microsoft Word", @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE");
    ShortcutManager.RegisterShortcut("excel",  "Microsoft Excel", @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE");
    ShortcutManager.RegisterShortcut("remote", "Sunlogin", @"C:\Program Files\Oray\SunLogin\SunloginClient\SunloginClient.exe");
    ShortcutManager.RegisterShortcut("sscom",  "SSCOM Serial", @"C:\Users\liu\OneDrive\Desktop\新建文件夹 (2)\sscom5.13.1.exe");
    ShortcutManager.RegisterShortcut("jmeter", "Apache JMeter", @"C:\Users\liu\OneDrive\Desktop\apache-jmeter-5.6.2\bin\jmeter.bat");
    ShortcutManager.RegisterShortcut("nes",    "小霸王游戏机", @"C:\Users\liu\Downloads\xbwmnq204\小霸王游戏机327合1\smynesc.exe");
}

void ShowList()
{
    CommandManager.ShowCommands();
    Console.WriteLine();
    ShortcutManager.ShowShortcuts();
    Console.WriteLine();
}

void EnterInteractiveMode()
{
    UI.PrintSuccess("交互模式已就绪");
    // 提示信息移除：保持界面干净
    
    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"{Environment.UserName}");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("@");
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.Write($"{Environment.MachineName}");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write(" $ ");
        Console.ResetColor();

        string? input;
        try
        {
            input = Console.ReadLine();
        }
        catch (IOException)
        {
            // Console input stream closed (e.g. piped host detached). Exit cleanly.
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(input)) continue;

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        CommandManager.ExecuteCommand(parts[0], parts.Skip(1).ToArray());
    }
}
