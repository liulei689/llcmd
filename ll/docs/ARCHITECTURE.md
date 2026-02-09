# ARCHITECTURE.md - LL CLI TOOL

## 项目结构
```
ll/
├── Program.cs          # 入口，配置，总时长
├── HotkeyManager.cs    # 全局热键管理
├── GuardianManager.cs  # 守护模式，界面渲染
├── PowerManager.cs     # 关机，空闲检测
├── UI.cs               # 打印方法
├── Utils.cs            # 工具函数
├── CommandManager.cs   # 命令注册执行
├── ShortcutManager.cs  # 快捷启动
├── LogManager.cs       # 日志
├── ListenManager.cs    # PG 监听
├── HistoryManager.cs   # 历史
├── NetSpeed.cs         # 网速
├── LanScanner.cs       # 局域网
├── QrCommands.cs       # QR
├── ElevationCommands.cs# 提权
├── SystemCommands.cs   # 系统
├── UtilityCommands.cs  # 实用
├── CodeStatsCommands.cs# 统计
├── VideoVaultCommands.cs# 视频加密
├── FileVaultCommands.cs# 文件加密
├── SM4Helper.cs        # SM4
├── SSHConn.cs          # SSH
├── NativeMethods.cs    # P/Invoke
├── ConfigManager.cs    # 配置管理器
├── NetMonCommands.cs   # 网络监控
├── FileAssocCommands.cs # 文件关联管理
└── config.json         # 配置
```

## 依赖图
```
Program
├── CommandManager
│   ├── QuickCommands, SystemCommands, etc.
├── LogManager
│   ├── Npgsql
├── PowerManager
│   ├── NativeMethods, Utils
├── GuardianManager
│   ├── Utils, RingSeries
Utils (独立)
```

## 关键组件
- **Program**: Main -> Initialize -> EnterInteractiveMode
- **GuardianManager**: Toggle -> Render (实时)
- **PowerManager**: StartSequence -> GetIdleTime
- **CommandManager**: Register -> Execute
- **LogManager**: Log to DB/File
- **Utils**: ParseTime, FormatRuntime

## 设计决策
- **异步**：所有后台任务 async。
- **模块化**：类职责单一。
- **配置**：JSON for 灵活。
- **安全**：加密敏感数据。
- **UI**：Console for 简单。

## 更新日志
- 2026-01-21：创建 ARCHITECTURE.md。