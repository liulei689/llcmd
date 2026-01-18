# LL CLI Tool - Professional Command Line Utility

[![.NET](https://img.shields.io/badge/.NET-10.0-blue)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey)](https://github.com/)

LL CLI Tool 是一款功能强大的专业命令行工具，集成了系统管理、文件加密、网络工具、快捷操作等66个指令，为开发者、系统管理员和普通用户提供便捷的命令行体验。

## ✨ 核心特性

- 🚀 **高性能**：基于 .NET 10，支持 AOT 编译，自包含发布
- 🔒 **安全加密**：AES-GCM 算法，支持视频和通用文件加密
- 🎯 **拖拽支持**：拖入文件自动处理，无需手动输入
- 📦 **批量操作**：支持目录、递归、通配符批量处理
- 🎨 **智能界面**：自动检测 VT 支持，彩色提示和进度显示
- 🛠️ **模块化设计**：易于扩展，插件化命令系统

## 📋 功能模块

### 系统管理 (1-19)
| 命令 | 描述 |
|------|------|
| `ls` / `list` / `help` | 显示指令清单 |
| `gd` | 守护模式 |
| `sd` | 倒计时关机 |
| `idle` | 空闲关机 |
| `st` | 任务状态 |
| `c` | 取消任务 |
| `abort` | 中止关机 |
| `myip` | 查看本机 IP |
| `netspeed` | 测当前网速 |
| `lan` | 扫描局域网设备 |
| `ip` | 网络信息 |
| `sys` | 系统信息 |
| `time` | 系统时间 |
| `settime` | 修改系统时间 |
| `admin` | 申请管理员权限 |

### 实用工具 (20-32)
| 命令 | 描述 |
|------|------|
| `ps` | 进程列表 |
| `kill` | 结束进程 |
| `port` | 端口检测 |
| `curl` | HTTP GET |
| `dns` | DNS 解析 |
| `find` | 查找文件 |
| `watch` | 监听目录 |
| `clip` | 剪贴板 |
| `path` | PATH 环境变量 |
| `env` | 环境变量 |
| `clean` | 清理 |
| `hist` | 历史记录 |
| `loc` | 统计目录代码行数 |

### 快捷操作 (40-53)
| 命令 | 描述 |
|------|------|
| `task` | 任务管理器 |
| `dev` | 设备管理器 |
| `ctrl` | 控制面板 |
| `set` | 系统设置 |
| `netset` | 网络设置 |
| `sound` | 声音设置 |
| `disp` | 显示设置 |
| `store` | 存储管理 |
| `desk` | 打开桌面 |
| `tmp` | 打开临时目录 |
| `recycle` | 回收站 |
| `snip` | 截图工具 |
| `dnsflush` | 清 DNS 缓存 |
| `netfix` | 网络修复 |

### 文件加密 (60-66)
| 命令 | 描述 |
|------|------|
| `encv` | 加密视频文件 (生成 .llv) |
| `playv` | 播放加密视频 (.llv) |
| `lsv` | 列出加密视频 (.llv) |
| `clrv` | 清理视频临时解密文件 |
| `decv` | 解密视频文件 (.llv 到 .mp4) |
| `encf` | 加密文件 (生成 .llf) |
| `decf` | 解密文件 (.llf 到原格式) |

### 快捷启动 (101-129)
| 命令 | 描述 |
|------|------|
| `vs` | Visual Studio |
| `code` | Visual Studio Code |
| `calc` | 计算器 |
| `notepad` | 记事本 |
| `edge` | Microsoft Edge |
| ... | 更多软件快捷启动 |

## 🚀 安装

### 从源码构建
```bash
# 克隆仓库
git clone https://gitee.com/forliu/llcmd.git
cd llcmd

# 构建
dotnet build -c Release

# 发布 (推荐)
dotnet publish -c Release -r win-x64 --self-contained
```

### 下载发布版本
从 [Release](https://gitee.com/forliu/llcmd/releases) 页面下载最新版本的 `ll.exe`。

## 📖 使用

### 基本使用
```bash
# 进入交互模式
ll

# 直接执行命令
ll myip
ll sys
```

### 文件加密示例
```bash
# 加密单个文件
ll encf secret.txt --hint=生日

# 批量加密目录
ll encf D:/documents --out=E:/vault -r

# 解密文件
ll decf secret.llf
```

### 拖拽使用
1. 运行 `ll` 进入交互模式
2. 将 `.llv` 或 `.llf` 文件拖入窗口
3. 按 Enter 键自动处理

### 系统管理示例
```bash
# 1小时后关机
ll sd 1h

# 空闲30分钟关机
ll idle 30m

# 查看网络信息
ll netspeed
```

## 🔧 技术栈

- **语言**：C# 14.0
- **框架**：.NET 10.0
- **加密**：AES-GCM + PBKDF2
- **发布**：AOT 编译，自包含
- **平台**：Windows x64

## 📁 项目结构

```
ll/
├── Program.cs          # 主入口
├── CommandManager.cs   # 命令管理
├── UI.cs              # 用户界面
├── Utils.cs           # 工具函数
├── VideoVaultCommands.cs  # 视频加密
├── FileVaultCommands.cs   # 文件加密
├── PowerManager.cs    # 关机管理
├── NetSpeed.cs        # 网速测试
└── ... (其他模块)
```

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

1. Fork 本仓库
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 创建 Pull Request

## 📄 许可证

本项目采用 MIT 许可证 - 查看 [LICENSE](LICENSE) 文件了解详情。

## 🙏 致谢

感谢所有贡献者和用户！

---

**LL CLI Tool** - 让命令行更强大！🚀
