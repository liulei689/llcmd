# docs/Functions.md - LL CLI TOOL

## 所有函数列表（按类分组）

### ConfigManager.cs
- `static T GetValue<T>(string keyPath, T defaultValue = default)`: 读取配置值。
- `static void SetValue(string keyPath, object value)`: 设置配置值。
- `static JsonObject GetConfig()`: 获取整个配置对象。

### Program.cs
- `static void Main(string[] args)`: 入口点。
- `static void Initialize()`: 注册命令。
- `static void EnterInteractiveMode(bool supportsVT)`: 交互模式。
- `static string? ReadLineWithEditing(string prompt)`: 编辑输入。
- `static int GetDisplayWidth(ReadOnlySpan<char> s)`: 获取显示宽度。
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
- `static void PasteText(string[] args)`: 粘贴文本。
- `static void UpdateConsoleTitle()`: 更新标题。
- `static void UpdateConfigTotalRuntime(long totalSeconds)`: 更新总时长。

### HotkeyManager.cs
- `static void Initialize()`: 初始化热键。
- `static void StartListening()`: 开始监听热键。
- `static void HandleHotkey()`: 处理热键，发送文本。
- `static void Cleanup()`: 清理热键。

### ClipboardMonitor (独立程序)
- `static void StartMonitoring()`: 启动剪贴板监控。
- `static void StopMonitoring()`: 停止剪贴板监控。
- `static void OnTimerElapsed(object sender, ElapsedEventArgs e)`: 定时检查剪贴板变化。
- `static void UpdatePresetText(string clipboardText)`: 根据剪贴板内容更新 preset.txt。
