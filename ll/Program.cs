using System.Text;
using System.Diagnostics;
using System.Net;
using System.Net.Mail;

// ==================================================================================
// LL CLI TOOL - Professional Edition
// ==================================================================================

// 环境配置
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
Console.Title = "LL Command Line Interface";

// 注册中心
var commands = new Dictionary<string, (string Description, Action<string[]> Action)>();
var shortcuts = new Dictionary<string, (string Description, string Path)>();

// 初始化配置
Initialize();

// 入口点
if (args.Length == 0)
{
    PrintBanner();
    EnterInteractiveMode();
}
else
{
    ExecuteCommand(args[0], args.Skip(1).ToArray());
}

// ----------------------------------------------------------------------------------
// 核心逻辑
// ----------------------------------------------------------------------------------

void Initialize()
{
    // 系统指令
    RegisterCommand("help", "查看指令清单 / List all commands", _ => ShowList()); // Added alias
    RegisterCommand("list", "查看指令清单 / List all commands", _ => ShowList());
    RegisterCommand("o",    "启动程序 / Open Program", args => OpenProgram(args));
    RegisterCommand("sd",   "倒计时关机 / Shutdown Timer", args => StartShutdownSequence(args));
    RegisterCommand("abort","取消系统关机 / Abort System Shutdown", _ => AbortSystemShutdown());
    RegisterCommand("size", "计算目录大小 / Calculate directory size", args => CheckDirectorySize(args));
    RegisterCommand("time", "系统时间 / System time", _ => ShowTime());
    RegisterCommand("sys",  "系统信息 / System Info", _ => ShowSysInfo());
    RegisterCommand("clr",  "清屏 / Clear screen", _ => Console.Clear());
    RegisterCommand("exit", "退出 / Exit", _ => Environment.Exit(0));

    // 快捷启动程序注册 (配合 'o' 指令使用)
    RegisterShortcut("vs",     "Visual Studio", "devenv"); 
    RegisterShortcut("vs22",   "Visual Studio 2022", @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe");
    RegisterShortcut("vs26",   "Visual Studio 2026", "devenv"); // 预留
    RegisterShortcut("code",   "Visual Studio Code", "code");
    RegisterShortcut("cmd",    "Command Prompt", "cmd");
    RegisterShortcut("calc",   "计算器", "calc");
    RegisterShortcut("notepad","记事本", "notepad");
    RegisterShortcut("edge",   "Microsoft Edge", "msedge");
    
    // User added shortcuts
    RegisterShortcut("cat",    "SakuraCat", @"C:\Program Files\SakuraCat\SakuraCat.exe");
    RegisterShortcut("unity",  "Unity Hub", @"D:\unityhuben\Unity Hub\Unity Hub.exe");
    RegisterShortcut("music",  "NetEase Music", @"D:\LenovoSoftstore\Install\wangyiyunyinyue\cloudmusic.exe");
    RegisterShortcut("word",   "Microsoft Word", @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE");
    RegisterShortcut("excel",  "Microsoft Excel", @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE");
    RegisterShortcut("remote", "Sunlogin", @"C:\Program Files\Oray\SunLogin\SunloginClient\SunloginClient.exe");
    RegisterShortcut("sscom",  "SSCOM Serial", @"C:\Users\liu\OneDrive\Desktop\新建文件夹 (2)\sscom5.13.1.exe");
    RegisterShortcut("jmeter", "Apache JMeter", @"C:\Users\liu\OneDrive\Desktop\apache-jmeter-5.6.2\bin\jmeter.bat");
}

void AbortSystemShutdown()
{
    try
    {
        Process.Start(new ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true, UseShellExecute = false });
        PrintSuccess("已发送取消关机指令 (Sent shutdown abort command)");
    }
    catch (Exception ex)
    {
        PrintError($"执行失败: {ex.Message}");
    }
}

void RegisterCommand(string name, string desc, Action<string[]> action)
{
    commands[name.ToLower()] = (desc, action);
}

void RegisterShortcut(string alias, string desc, string path)
{
    shortcuts[alias.ToLower()] = (desc, path);
}

void ExecuteCommand(string cmdInput, string[] args)
{
    string cmd = cmdInput.ToLower();

    // 1. 查找系统指令
    if (commands.TryGetValue(cmd, out var commandInfo))
    {
        commandInfo.Action(args);
        return;
    }

    // 2. 未知指令
    PrintError($"未知指令: '{cmd}'");
    PrintInfo("输入 'list' 查看可用指令。");
}

void OpenProgram(string[] args)
{
    if (args.Length == 0)
    {
        PrintError("请指定要打开的程序名称");
        PrintInfo("示例: o vs, o code, o notepad");
        return;
    }

    string key = args[0].ToLower();
    string[] programArgs = args.Skip(1).ToArray();

    if (shortcuts.TryGetValue(key, out var shortcutInfo))
    {
        LaunchProgram(shortcutInfo.Description, shortcutInfo.Path, programArgs);
    }
    else
    {
        PrintError($"未找到程序配置: '{key}'");
        PrintInfo("输入 'list' 查看支持的程序列表");
    }
}

void StartShutdownSequence(string[] args)
{
    // Help / Usage check
    if (args.Length > 0 && (args[0] == "?" || args[0].ToLower() == "help"))
    {
        PrintInfo("=== 倒计时关机使用说明 ===");
        PrintInfo("基本用法: sd [时间][单位]");
        PrintInfo("  sd        -> 进入交互设置模式");
        PrintInfo("  sd 30m    -> 30 分钟");
        PrintInfo("  sd 1h     -> 1 小时");
        PrintInfo("  sd 120s   -> 120 秒");
        PrintInfo("  sd 1.5    -> 1.5 小时");
        return;
    }

    double totalSeconds = 7200; // 默认2小时

    if (args.Length > 0)
    {
        string input = args[0].ToLower().Trim();
        if (!TryParseTime(input, out totalSeconds))
        {
             PrintError($"时间格式错误: '{input}'");
             PrintInfo("示例: sd 30m (30分钟), sd 1.5h (1.5小时), sd 300s (秒)");
             return;
        }
    }
    else
    {
        // 交互式输入时间
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("请输入倒计时时间 (默认 2h, 直接回车使用默认值): ");
        Console.ResetColor();
        string? input = Console.ReadLine();
        
        if (!string.IsNullOrWhiteSpace(input))
        {
            if (!TryParseTime(input, out totalSeconds))
            {
                PrintError("时间格式无效，操作已取消。");
                return;
            }
        }
    }

    TimeSpan duration = TimeSpan.FromSeconds(totalSeconds);
    DateTime targetTime = DateTime.Now.Add(duration);

    PrintHeader("SHUTDOWN SEQUENCE INITIATED");
    PrintResult("Duration", duration.ToString(@"hh\:mm\:ss"));
    PrintResult("Target Time", targetTime.ToString("yyyy-MM-dd HH:mm:ss"));
    PrintInfo("Press [ESC] or [Ctrl+C] to cancel...");
    Console.WriteLine();

    bool cancelled = false;
    Console.CursorVisible = false;

    // 捕获 Ctrl+C 以便优雅退出倒计时
    ConsoleCancelEventHandler cancelHandler = (sender, e) => {
        e.Cancel = true; // 防止程序直接退出
        cancelled = true;
    };
    Console.CancelKeyPress += cancelHandler;

    try
    {
        while (DateTime.Now < targetTime && !cancelled)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape)
                {
                    cancelled = true;
                    break;
                }
            }

            TimeSpan remaining = targetTime - DateTime.Now;
            
            string timeStr = remaining.ToString(@"hh\:mm\:ss");
            Console.ForegroundColor = remaining.TotalSeconds <= 60 ? ConsoleColor.Red : ConsoleColor.Green;
            Console.Write($"\r >> SYSTEM SHUTDOWN IN: {timeStr}   ");
            Console.ResetColor();
            
            Thread.Sleep(100);
        }
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
        Console.CursorVisible = true;
        Console.WriteLine();
    }

    if (cancelled)
    {
        PrintSuccess("倒计时已取消 (Countdown Cancelled)");
    }
    else
    {
        PrintInfo("Timer reached zero. Executing shutdown protocols...");
        SendEmailTo($"Shutdown initiated automatically at {DateTime.Now}");
        
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
        }
        catch (Exception ex)
        {
            PrintError($"Failed to execute shutdown command: {ex.Message}");
        }
    }
}

bool TryParseTime(string input, out double totalSeconds)
{
    totalSeconds = 0;
    input = input.ToLower().Trim();
    double val = 0;

    try 
    {
        if (input.EndsWith("s") && double.TryParse(input[..^1], out val))
            totalSeconds = val;
        else if (input.EndsWith("m") && double.TryParse(input[..^1], out val))
            totalSeconds = val * 60;
        else if (input.EndsWith("h") && double.TryParse(input[..^1], out val))
            totalSeconds = val * 3600;
        else if (double.TryParse(input, out val))
            totalSeconds = val * 3600; // 默认为小时
        else
            return false;
            
        return true;
    }
    catch 
    {
        return false;
    }
}

void SendEmailTo(string text)
{
    PrintInfo("Sending notification email...");
    try
    {
        using (MailMessage mailMessage = new MailMessage())
        using (SmtpClient smtpClient = new SmtpClient("smtp.qq.com", 587))
        {
            mailMessage.To.Add("799942292@qq.com"); 
            mailMessage.Body = text;
            mailMessage.IsBodyHtml = true;
            mailMessage.BodyEncoding = Encoding.UTF8;
            mailMessage.From = new MailAddress("1243500742@qq.com", "给您关机啦！");
            mailMessage.Subject = "计算机名: " + System.Environment.MachineName + " - " + DateTime.Now.ToString();
            mailMessage.SubjectEncoding = Encoding.UTF8;
            
            smtpClient.EnableSsl = true;
            // 提示：请确保授权码正确，此处使用用户提供的掩码
            smtpClient.Credentials = new NetworkCredential("1243500742@qq.com", "***");
            
            smtpClient.Send(mailMessage);
            PrintSuccess("Email sent successfully!");
        }
    }
    catch (Exception ex) 
    { 
        PrintError($"Email failed: {ex.Message}");
        try 
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "log.txt"), $"{DateTime.Now}: {ex.Message}\r\n"); 
        }
        catch { }
    }
}

void EnterInteractiveMode()
{
    PrintSuccess("交互模式已就绪 (Interactive Mode Ready)");
    PrintInfo("Tips: 输入 'help' 查看帮助，输入 'sd 30m' 设定30分钟后关机");
    
    while (true)
    {
        // 优化提示符样式： user@machine $ 
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"{Environment.UserName}");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("@");
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.Write($"{Environment.MachineName}");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write(" $ ");
        Console.ResetColor();

        string? input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input)) continue;

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        ExecuteCommand(parts[0], parts.Skip(1).ToArray());
    }
}

// ----------------------------------------------------------------------------------
// 功能实现
// ----------------------------------------------------------------------------------

void LaunchProgram(string name, string path, string[] args)
{
    PrintInfo($"正在启动 [ {name} ] ...");
    try
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true, // 允许通过系统Shell启动 (支持文件名查找)
            Arguments = string.Join(" ", args)
        };
        Process.Start(psi);
        PrintSuccess("启动成功!");
    }
    catch (System.ComponentModel.Win32Exception)
    {
        PrintError($"启动失败: 找不到文件或命令 '{path}'");
        PrintInfo("提示: 请在代码 'Initialize' 方法中检查路径配置。");
    }
    catch (Exception ex)
    {
        PrintError($"启动错误: {ex.Message}");
    }
}

void ShowList()
{
    PrintHeader("功能指令 (Commands)");
    foreach (var cmd in commands)
    {
        PrintItem(cmd.Key, cmd.Value.Description);
    }
    Console.WriteLine();

    PrintHeader("程序清单 (Use 'o' to launch)");
    foreach (var sc in shortcuts)
    {
        PrintItem(sc.Key, $"{sc.Value.Description}");
    }
    Console.WriteLine();
}

void CheckDirectorySize(string[] args)
{
    string path = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
    if (!Directory.Exists(path))
    {
        PrintError($"路径不存在: {path}");
        return;
    }

    PrintInfo($"正在分析: {path} ...");
    try
    {
        Stopwatch sw = Stopwatch.StartNew();
        long size = GetDirectorySize(new DirectoryInfo(path));
        sw.Stop();

        PrintResult("目标路径", path);
        PrintResult("总大小", FormatSize(size));
        PrintResult("耗时", $"{sw.ElapsedMilliseconds} ms");
    }
    catch (Exception ex)
    {
        PrintError(ex.Message);
    }
}

long GetDirectorySize(DirectoryInfo d)
{
    long size = 0;
    try
    {
        FileInfo[] fis = d.GetFiles();
        foreach (FileInfo fi in fis) size += fi.Length;
        
        DirectoryInfo[] dis = d.GetDirectories();
        foreach (DirectoryInfo di in dis) size += GetDirectorySize(di);
    }
    catch { /* 忽略权限错误 */ }
    return size;
}

void ShowTime()
{
    PrintResult("当前时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss dddd"));
}

void ShowSysInfo()
{
    PrintResult("OS Version", Environment.OSVersion.ToString());
    PrintResult("Machine", Environment.MachineName);
    PrintResult("Cores", Environment.ProcessorCount.ToString());
    PrintResult("Runtime", Environment.Version.ToString());
}

string FormatSize(long bytes)
{
    string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
    int counter = 0;
    decimal number = (decimal)bytes;
    while (Math.Round(number / 1024) >= 1)
    {
        number /= 1024;
        counter++;
    }
    return string.Format("{0:n1} {1}", number, suffixes[counter]);
}

// ----------------------------------------------------------------------------------
// UI 样式工具
// ----------------------------------------------------------------------------------

void PrintBanner()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(@"
██╗      ██╗           ██████╗██╗     ██╗
██║      ██║          ██╔════╝██║     ██║
██║      ██║          ██║     ██║     ██║
██║      ██║          ██║     ██║     ██║
███████╗ ███████╗     ╚██████╗███████╗██║
╚══════╝ ╚══════╝      ╚═════╝╚══════╝╚═╝
");
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("============================================================");
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($" SESSION ID : {Guid.NewGuid().ToString().Split('-')[0].ToUpper()} | USER: {Environment.UserName}");
    Console.WriteLine($" SYSTEM     : {Environment.OSVersion} | .NET: {Environment.Version}");
    Console.WriteLine($" TIMESTAMP  : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("============================================================");
    Console.ResetColor();
    Console.WriteLine();
}

void PrintHeader(string title)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"[{title}]");
    Console.WriteLine(new string('-', 40));
    Console.ResetColor();
}

void PrintItem(string key, string desc)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write($" {key.PadRight(12)}");
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"| {desc}");
    Console.ResetColor();
}

void PrintResult(string label, string value)
{
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write($" {label.PadRight(15)}: ");
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(value);
    Console.ResetColor();
}

void PrintInfo(string msg)
{
    Console.ForegroundColor = ConsoleColor.Gray;
    Console.WriteLine($"[INFO] {msg}");
    Console.ResetColor();
}

void PrintSuccess(string msg)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[OK]   {msg}");
    Console.ResetColor();
}

void PrintError(string msg)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FAIL] {msg}");
    Console.ResetColor();
}
