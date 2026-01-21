# README.md - LL CLI TOOL

## 项目概述
LL CLI TOOL 是一个强大的命令行工具，专为 Windows 系统设计，使用 .NET 10 和 C# 14.0 构建。提供实时系统监控、守护模式、电源管理、数据库监听、文件加密、网络工具等功能。让你的命令行体验更加高效、安全和智能化。

## 主要功能
- **守护模式**：实时监控 CPU、内存、磁盘、网络，显示系统状态。
- **电源管理**：自动关机、锁屏、空闲检测。
- **命令系统**：50+ 内置命令，支持自定义。
- **数据库集成**：PostgreSQL 监听和通知。
- **文件加密**：SM4 算法加密视频和文件。
- **网络工具**：网速测量、局域网扫描、DNS 解析。
- **历史记录**：命令历史和会话管理。

## 快速开始
1. **克隆仓库**：
   ```bash
   git clone https://gitee.com/forliu/llcmd
   cd llcmd
   ```

2. **构建项目**：
   ```bash
   dotnet build
   ```

3. **运行程序**：
   ```bash
   dotnet run
   ```

4. **配置**：
   编辑 `config.json` 设置数据库、SSH、邮箱等。

5. **使用**：
   - 输入 `gd` 进入守护模式。
   - 输入 `sd 30m` 30分钟后关机。
   - 输入 `help` 查看所有命令。

## 系统要求
- Windows 10/11
- .NET 10 SDK
- PostgreSQL (可选)

## 架构
- **入口**：Program.cs
- **核心模块**：GuardianManager (守护), PowerManager (电源), CommandManager (命令)
- **工具**：Utils, UI, LogManager
- **外部依赖**：Npgsql, System.Runtime.InteropServices

## 贡献
见 [CONTRIBUTING.md](CONTRIBUTING.md)

## 许可证
MIT License

## 更新日志
见 [CHANGELOG.md](CHANGELOG.md)

---
*最后更新：2026-01-21*
