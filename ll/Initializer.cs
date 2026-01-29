using System;
using System.Diagnostics;

namespace LL
{
    // 将大量的命令注册抽离到单独文件，便于查看与维护
    internal static class Initializer
    {
        public static void Initialize()
        {
            // 系统指令
            CommandManager.RegisterCommand(1,  "ls",   "指令清单", _ => Program.ShowList());
            CommandManager.RegisterCommand(2,  "gd",   "守护模式", args => GuardianManager.ToggleGuardianMode(args));
            CommandManager.RegisterCommand(3,  "sd",   "倒数关机", args => PowerManager.StartShutdownSequence(args));
            CommandManager.RegisterCommand(4,  "idle", "空闲关机", args => PowerManager.StartIdleMonitor(args));
            CommandManager.RegisterCommand(5,  "st",   "任务状态", _ => Program.ShowAllStatus());
            CommandManager.RegisterCommand(6,  "c",    "取消任务", _ => TaskManager.CancelLatest());
            CommandManager.RegisterCommand(7,  "abort","中止关机", _ => PowerManager.AbortSystemShutdown());

            CommandManager.RegisterCommand(8,  "myip", "查看本机 IP", _ => QuickCommands.ShowMyIp());
            CommandManager.RegisterCommand(9,  "netspeed", "测当前网速", args => NetSpeed.Measure(args));
            CommandManager.RegisterCommand(10, "lan",  "扫描局域网设备", args => LanScanner.Scan(args));
            CommandManager.RegisterCommand(11, "ip",   "网络信息", _ => UtilityCommands.Execute(new[] {"ip"}));

            CommandManager.RegisterCommand(12, "open", "启动程序", args => ShortcutManager.OpenProgram(args));
            CommandManager.RegisterCommand(13, "sys",  "系统信息", args => SystemCommands.ShowSysInfo(args));
            CommandManager.RegisterCommand(14, "time", "系统时间", args => SystemCommands.ShowTime(args));
            CommandManager.RegisterCommand(15, "size", "目录大小", args => SystemCommands.CheckDirectorySize(args));
            CommandManager.RegisterCommand(16, "clr",  "清屏", _ => Console.Clear());
            CommandManager.RegisterCommand(17, "qr",   "生成二维码", args => QrCommands.Print(args));
            CommandManager.RegisterCommand(18, "admin", "申请管理员权限", args => ElevationCommands.Elevate(args));
            CommandManager.RegisterCommand(19, "settime", "修改系统时间", args => SystemCommands.SetTime(args));

            // 实用工具（扁平化：直接用命令名，不用 util 前缀）
            CommandManager.RegisterCommand(20, "ps", "进程列表", args => UtilityCommands.Execute(new[] {"ps"}));
            CommandManager.RegisterCommand(21, "kill", "结束进程", args => UtilityCommands.Execute(new[] {"kill"}));
            CommandManager.RegisterCommand(22, "port", "端口检测", args => UtilityCommands.Execute(new[] {"port"}));
            // ip 已在上面手动编号
            CommandManager.RegisterCommand(23, "curl", "HTTP GET", args => UtilityCommands.Execute(new[] {"curl"}));
            CommandManager.RegisterCommand(24, "dns", "DNS 解析", args => UtilityCommands.Execute(new[] {"dns"}));
            CommandManager.RegisterCommand(25, "find", "查找文件", args => UtilityCommands.Execute(new[] {"find"}));
            CommandManager.RegisterCommand(26, "watch", "监听目录", args => UtilityCommands.Execute(new[] {"watch"}));
            CommandManager.RegisterCommand(27, "clip", "剪贴板", args => UtilityCommands.Execute(new[] {"clip"}));
            CommandManager.RegisterCommand(28, "path", "PATH", args => UtilityCommands.Execute(new[] {"path"}));
            CommandManager.RegisterCommand(29, "env", "环境变量", args => UtilityCommands.Execute(new[] {"env"}));
            CommandManager.RegisterCommand(30, "clean", "清理", args => UtilityCommands.Execute(new[] {"clean"}));
            CommandManager.RegisterCommand(31, "hist", "历史记录", args => HistoryCommands.Show(args));
            CommandManager.RegisterCommand(32, "loc", "统计目录代码行数", args => CodeStatsCommands.Run(args));
            CommandManager.RegisterCommand(33, "template", "生成项目模板", args => TemplateCommands.Run(args));
            CommandManager.RegisterCommand(34, "volume", "音量控制: mute, unmute, up, down, set <level>", args => VolumeCommands.Run(args));
            CommandManager.RegisterCommand(35, "lottery", "打开年会抽奖页面", args => LotteryCommands.Run(args));

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
            CommandManager.RegisterCommand(54, "folder", "打开程序文件夹", _ => Process.Start("explorer.exe", AppContext.BaseDirectory));

            CommandManager.RegisterCommand(55, "home", "回到首页", _ => Program.ShowHome());
            CommandManager.RegisterCommand(56, "net", "网络控制", args => NetworkCommands.ControlNetwork(args));
            CommandManager.RegisterCommand(57, "server", "服务器检测", args => ServerChecker.CheckServers(args).GetAwaiter().GetResult());
            CommandManager.RegisterCommand(58, "encoding", "检测文件或文件夹编码", args => EncodingDetector.DetectEncoding(args));
            CommandManager.RegisterCommand(60, "image", "分析图片像素和颜色", args => ImageAnalyzer.AnalyzeImage(args));
            CommandManager.RegisterCommand(61, "readbin", "读取文件二进制内容", args => BinaryFileReader.ReadBinary(args));

            CommandManager.RegisterCommand(62, "encv", "加密视频文件(生成 .llv)", args => VideoVaultCommands.Encrypt(args));
            CommandManager.RegisterCommand(63, "playv", "播放加密视频(.llv)", args => VideoVaultCommands.Play(args));
            CommandManager.RegisterCommand(64, "lsv", "列出加密视频(.llv)", args => VideoVaultCommands.List(args));
            CommandManager.RegisterCommand(65, "clrv", "清理视频临时解密文件", args => VideoVaultCommands.CleanTemp(args));
            CommandManager.RegisterCommand(66, "decv", "解密视频文件(.llv 到 .mp4)", args => VideoVaultCommands.Decrypt(args));
            CommandManager.RegisterCommand(67, "encf", "加密文件(生成 .llf)", args => FileVaultCommands.EncryptFile(args));
            CommandManager.RegisterCommand(68, "decf", "解密文件(.llf 到原格式)", args => FileVaultCommands.DecryptFile(args));
            CommandManager.RegisterCommand(69, "zip", "压缩文件夹", args => ZipManager.Compress(args));
            CommandManager.RegisterCommand(70, "unzip", "解压ZIP文件", args => ZipManager.Uncompress(args));

            // 开机启动管理
            CommandManager.RegisterCommand(71, "autostart", "开机启动管理", args => Program.ManageAutoStart(args));

            // PostgreSQL 监听通知
            CommandManager.RegisterCommand(72, "listen", "监听PostgreSQL通知", args => Program.ListenPostgreSQL(args));
            CommandManager.RegisterCommand(73, "notify", "发送PostgreSQL通知", args => Program.NotifyPostgreSQL(args));
            CommandManager.RegisterCommand(74, "unlisten", "停止监听PostgreSQL通知", _ => Program.UnlistenPostgreSQL());

            CommandManager.RegisterCommand(80, "encrypt", "加密", args => Program.EncryptCommand(args));
            CommandManager.RegisterCommand(81, "decrypt", "解密", args => Program.DecryptCommand(args));
            CommandManager.RegisterCommand(82, "hexview", "查看Hex字符串", args => HexViewer.ViewHex(args));
            CommandManager.RegisterCommand(83, "git", "精简 Git 操作: history, info, rollback, help — 用法: git <子命令> [参数]", args => GitCommandHandler.Handle(args));
            CommandManager.RegisterCommand(84, "cd", "设置默认项目目录（后续 git 命令在该目录执行）。用法: cd <path>", args => GitCommandHandler.SetDefaultProject(args));
            CommandManager.RegisterCommand(85, "proxy", "检测系统代理设置 (proxy check)", args => ProxyCommands.CheckProxy(args));
            CommandManager.RegisterCommand(86, "key", "密钥管理: add <name> <value>, get <name>, list, remove <name>, import <csv_file>, search <keyword>", args => KeyCommandHandler.Handle(args));
            CommandManager.RegisterCommand(87, "batch-rename", "批量重命名/收集文件: rename <dir> [opts] 或 collect <dir> <level> <newfolder>", args => BatchRenameCommand.Handle(args));
            CommandManager.RegisterCommand(88, "json-validate", "校验 JSON 格式: <json_string> 或 --file <file_path>", args => JsonValidator.Handle(args));
            CommandManager.RegisterCommand(89, "ssh", "SSH 操作: exec <cmd>, shell, upload <local> <remote>, download <remote> <local>, status", args => RemoteCommands.HandleSSH(args));
            CommandManager.RegisterCommand(90, "eventlog", "查看 Windows 事件日志 (eventlog [filter|open])", args => EventLogViewer.ViewEventLog(args));
            CommandManager.RegisterCommand(91, "sync", "文件夹同步: sync <source> <target> 或 sync stop", args => SyncManager.HandleSync(args));
            CommandManager.RegisterCommand(92, "hash", "计算哈希: hash <algorithm> <text> 或 hash <algorithm> --file <file>", args => HashCalculator.Handle(args));
            CommandManager.RegisterCommand(93, "passwd", "生成随机密码: passwd [length] [--no-symbols] [--no-numbers]", args => PasswordGenerator.Handle(args));
            CommandManager.RegisterCommand(94, "base64", "Base64 编码/解码: base64 <encode|decode> <text> 或 --file <file>", args => Base64Tool.Handle(args));
            CommandManager.RegisterCommand(95, "dice", "掷骰子: dice [sides] [count]", args => DiceRoller.Handle(args));
            CommandManager.RegisterCommand(96, "art", "生成 ASCII 艺术文字: art <text>", args => AsciiArt.Handle(args));
            CommandManager.RegisterCommand(99, "exit", "退出", _ => Environment.Exit(0));
            CommandManager.RegisterCommand(100, "min", "最小化窗口", _ => { IntPtr hWnd = LL.Native.NativeMethods.GetConsoleWindow(); LL.Native.NativeMethods.ShowWindow(hWnd, LL.Native.NativeMethods.SW_MINIMIZE); });

            // 快捷启动程序注册
            ShortcutManager.RegisterShortcut(101, "vs",     "Visual Studio", "devenv");
            ShortcutManager.RegisterShortcut(102, "vs22",   "Visual Studio 2022", @"C:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\Common7\\IDE\\devenv.exe");
            ShortcutManager.RegisterShortcut(103, "vs26",   "Visual Studio 2026", "devenv");
            ShortcutManager.RegisterShortcut(104, "code",   "Visual Studio Code", "code");
            ShortcutManager.RegisterShortcut(105, "cmd",    "Command Prompt", "cmd");
            ShortcutManager.RegisterShortcut(106, "calc",   "计算器", "calc");
            ShortcutManager.RegisterShortcut(107, "notepad","记事本", "notepad");
            ShortcutManager.RegisterShortcut(108, "edge",   "Microsoft Edge", "msedge");
            
            ShortcutManager.RegisterShortcut(121, "cat",    "SakuraCat", @"C:\\Program Files\\SakuraCat\\SakuraCat.exe");
            ShortcutManager.RegisterShortcut(122, "unity",  "Unity Hub", @"D:\\unityhuben\\Unity Hub\\Unity Hub.exe");
            ShortcutManager.RegisterShortcut(123, "music",  "NetEase Music", @"D:\\LenovoSoftstore\\Install\\wangyiyunyinyue\\cloudmusic.exe");
            ShortcutManager.RegisterShortcut(124, "word",   "Microsoft Word", @"C:\\Program Files\\Microsoft Office\\root\\Office16\\WINWORD.EXE");
            ShortcutManager.RegisterShortcut(125, "excel",  "Microsoft Excel", @"C:\\Program Files\\Microsoft Office\\root\\Office16\\EXCEL.EXE");
            ShortcutManager.RegisterShortcut(126, "remote", "Sunlogin", @"C:\\Program Files\\Oray\\SunLogin\\SunloginClient\\SunloginClient.exe");
            ShortcutManager.RegisterShortcut(127, "sscom",  "SSCOM Serial", @"C:\\Users\\liu\\OneDrive\\Desktop\\新建文件夹 (2)\\sscom5.13.1.exe");
            ShortcutManager.RegisterShortcut(128, "jmeter", "Apache JMeter", @"C:\\Users\\liu\\OneDrive\\Desktop\\apache-jmeter-5.6.2\\bin\\jmeter.bat");
            ShortcutManager.RegisterShortcut(129, "nes",    "小霸王游戏机", @"C:\\Users\\liu\\Downloads\\xbwmnq204\\小霸王游戏机327合1\\smynesc.exe");

            // 快捷启动使用纯数字(101-129) 或 open + 别名
        }
    }
}
