# docs/Functions.md - LL CLI TOOL

## 所有函数列表（按类分组）

### Program.cs
- `static void Main(string[] args)`: 入口点。
- `static void Initialize()`: 注册命令。
- `static void EnterInteractiveMode(bool supportsVT)`: 交互模式。
- `static string? ReadLineWithEditing(string prompt)`: 编辑输入。
- `static void UpdateConsoleTitle()`: 更新标题。
- `static void UpdateConfigTotalRuntime(long totalSeconds)`: 更新总时长。
- `static string FormatRuntime(long seconds)`: 格式化时长。
- `static void HideConsoleButtons()`: 隐藏按钮。
- `static bool EnableVirtualTerminalProcessing()`: 启用 VT。
- `static void ManageAutoStart(string[] args)`: 管理启动。
- `static bool IsInStartup()`: 检查启动。
- `static void AddToStartup()`: 添加启动。
- `static void RemoveFromStartup()`: 移除启动。
- `static void ShowAllStatus()`: 显示状态。
- `static async void ListenPostgreSQL(string[] args)`: 监听 PG。
- `static void UnlistenPostgreSQL()`: 停止监听。
- `static async void NotifyPostgreSQL(string[] args)`: 发送通知。
- `static bool IsValidChannelName(string name)`: 验证频道。
- `static string GetConnectionString()`: 获取连接字符串。
- `static void EncryptCommand(string[] args)`: 加密命令。
- `static void UpdatePassword(string keyPath, string newValue)`: 更新密码。
- `static string UpdateJsonValue(string json, string keyPath, string newValue)`: 更新 JSON。
- `static void DecryptCommand(string[] args)`: 解密命令。

### GuardianManager.cs
- `static void ToggleGuardianMode(string[] args)`: 切换守护。
- `static bool IsActive { get; }`: 是否激活。
- `static void StartGuardianMode()`: 启动守护。
- `static void StopGuardianMode()`: 停止守护。
- `static void EnqueueEvent(string text)`: 入队事件。
- `static string GetEventKey(string text)`: 获取事件键。
- `static async Task KeyListenerLoop(CancellationToken token)`: 按键监听。
- `static async Task EventProbeLoop(CancellationToken token)`: 事件探测。
- `static async Task DashboardMasterLoop(CancellationToken token)`: 主循环。
- `static void RenderGuardianDashboard(int w, int h, long tick, List<string> logs, RingSeries memSeries, RingSeries thdSeries, RingSeries diskSeries)`: 渲染仪表板。
- `static (long TotalRx, long TotalTx) SnapshotNicBytes()`: 快照网络字节。
- `static void DrawAsciiPanel(int x, int y, int width, int height, string title)`: 绘制面板。
- `static void WriteAt(int x, int y, string text)`: 在位置写文本。
- `static DriveInfo? GetPrimaryDriveSafe()`: 获取主驱动器。
- `static int GetPrimaryDriveFreePercent()`: 获取空闲百分比。
- `static double GetDriveFreePercent(DriveInfo d)`: 计算百分比。
- `class RingSeries`: 环形系列。
  - `RingSeries(int capacity)`: 构造函数。
  - `int Latest { get; }`: 最新值。
  - `void Add(int value)`: 添加值。
  - `string RenderSparkline(int width)`: 渲染火花线。

### PowerManager.cs
- `static double IdleEnterSeconds { get; set; }`: 空闲进入秒。
- `static double IdleExitSeconds { get; set; }`: 空闲退出秒。
- `static double IdleLockSeconds { get; set; }`: 锁屏秒。
- `static void StartShutdownSequence(string[] args)`: 启动关机序列。
- `static void StartIdleMonitor(string[] args)`: 启动空闲监听。
- `static void ShowStatus()`: 显示状态。
- `static void CancelTask()`: 取消任务。
- `static void AbortSystemShutdown()`: 中止关机。
- `static void ExecuteShutdown()`: 执行关机。
- `static uint GetIdleTime()`: 获取空闲时间。

### Utils.cs
- `static bool TryParseTime(string input, out double totalSeconds)`: 解析时间。
- `static void SendEmailTo(string subject, string body)`: 发送邮件。
- `static string FormatRuntime(long seconds)`: 格式化运行时长。
- `static string FormatSize(long bytes)`: 格式化大小。
- `static string Truncate(string text, int maxLength)`: 截断文本。

### CommandManager.cs
- `static void RegisterCommand(int id, string name, string desc, Action<string[]> action)`: 注册命令。
- `static void ExecuteCommand(string name, string[] args)`: 执行命令。
- `static void ShowCommands()`: 显示命令。

### ShortcutManager.cs
- `static void RegisterShortcut(int id, string alias, string desc, string command)`: 注册快捷方式。
- `static void OpenProgram(string[] args)`: 打开程序。
- `static void ShowShortcuts()`: 显示快捷方式。

### LogManager.cs
- `static void Initialize(string connString)`: 初始化日志。
- `static void Log(string level, string category, string message)`: 记录日志。

### ListenManager.cs
- `static void StartListen(string channel, string connString)`: 启动监听。
- `static void StopListen()`: 停止监听。
- `static bool IsListening { get; }`: 是否监听。
- `static string CurrentChannel { get; }`: 当前频道。

### HistoryManager.cs
- `static void Add(string input)`: 添加历史。
- `static string? Prev()`: 上一个历史。
- `static string? Next()`: 下一个历史。
- `static void EnsureSessionLoaded()`: 加载会话。

### NetSpeed.cs
- `static void Measure(string[] args)`: 测量网速。

### LanScanner.cs
- `static void Scan(string[] args)`: 扫描局域网。

### QrCommands.cs
- `static void Print(string[] args)`: 生成 QR。

### ElevationCommands.cs
- `static void Elevate(string[] args)`: 提权执行。

### SystemCommands.cs
- `static void ShowSysInfo(string[] args)`: 显示系统信息。
- `static void ShowTime(string[] args)`: 显示时间。
- `static void CheckDirectorySize(string[] args)`: 检查目录大小。
- `static void SetTime(string[] args)`: 设置时间。

### UtilityCommands.cs
- `static void Execute(string[] args)`: 执行实用命令。

### CodeStatsCommands.cs
- `static void Run(string[] args)`: 运行代码统计。

### VideoVaultCommands.cs
- `static void Encrypt(string[] args)`: 加密视频。
- `static void Play(string[] args)`: 播放视频。
- `static void List(string[] args)`: 列出视频。
- `static void CleanTemp(string[] args)`: 清理临时。
- `static void Decrypt(string[] args)`: 解密视频。

### FileVaultCommands.cs
- `static void EncryptFile(string[] args)`: 加密文件。
- `static void DecryptFile(string[] args)`: 解密文件。

### SM4Helper.cs
- `static string Encrypt(string plainText)`: 加密。
- `static string Decrypt(string cipherText)`: 解密。

### SSHConn.cs
- `SSHConn()`: 构造函数。
- `bool OpenDbPort()`: 打开 DB 端口。
- `int LocalPort { get; }`: 本地端口。

### NativeMethods.cs
- 各种 P/Invoke 方法，如 GetLastInputInfo, ShowWindow 等。

### UI.cs
- `static void PrintHeader(string text)`: 打印标题。
- `static void PrintInfo(string text)`: 打印信息。
- `static void PrintError(string text)`: 打印错误。
- `static void PrintSuccess(string text)`: 打印成功。
- `static void PrintResult(string label, string value)`: 打印结果。

### QuickCommands.cs
- `static void ShowMyIp()`: 显示 IP。
- `static void OpenTaskManager()`: 打开任务管理器。
- `static void OpenDeviceManager()`: 打开设备管理器。
- `static void OpenControlPanel()`: 打开控制面板。
- `static void OpenSettings()`: 打开设置。
- `static void OpenNetworkSettings()`: 打开网络设置。
- `static void OpenSoundSettings()`: 打开声音设置。
- `static void OpenDisplaySettings()`: 打开显示设置。
- `static void OpenStorageSettings()`: 打开存储设置。
- `static void OpenDesktopFolder()`: 打开桌面。
- `static void OpenTempFolder()`: 打开临时目录。
- `static void OpenRecycleBin()`: 打开回收站。
- `static void OpenSnippingTool()`: 打开截图工具。
- `static void FlushDns()`: 清 DNS 缓存。
- `static void NetFix()`: 网络修复。

### TaskManager.cs
- `static void Register(string name, CancellationTokenSource cts)`: 注册任务。
- `static void CancelLatest()`: 取消最新任务。
- `static (string Name, DateTime? StartedAt) GetLatest()`: 获取最新任务。
- `static void Clear(CancellationTokenSource cts)`: 清除任务。

### HistoryCommands.cs
- `static void Show(string[] args)`: 显示历史。

### ConfigManager.cs
- `static T GetValue<T>(string keyPath, T defaultValue = default)`: 读取配置值。
- `static void SetValue(string keyPath, object value)`: 设置配置值。
- `static JsonObject GetConfig()`: 获取整个配置对象。

## 复用建议
- 使用 Utils.TryParseTime for 时间解析。
- 使用 UI.Print* for 输出。
- 使用 LogManager.Log for 日志。
- 使用 SM4Helper for 加密。
- 使用 NativeMethods for Windows API。