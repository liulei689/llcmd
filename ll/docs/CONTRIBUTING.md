# CONTRIBUTING.md - LL CLI TOOL

## 贡献指南
欢迎贡献！请遵循以下指南。

### 编码标准
- **命名**：PascalCase for 类/方法/属性；camelCase for 变量/参数。
- **注释**：XML 注释 for 公共方法；行注释 for 复杂逻辑。
- **异常**：try-catch 包围可能出错代码；不吞没异常。
- **异步**：所有 I/O 使用 async/await；CancellationToken。
- **风格**：4 空格缩进；最大行长 120。

### 架构约束
- **模块化**：每个功能独立类；低耦合。
- **依赖**：见 ARCHITECTURE.md。
- **配置**：config.json；热重载。
- **日志**：LogManager.Log；级别 Info/Error。
- **测试**：build 无错误；功能测试。

### 修改流程
1. 修改代码；更新文档，更新约束和各种md文件。
2. 测试：run_build；运行程序。


### 常见模式
- **新命令**：RegisterCommand in Initialize。
- **新界面**：WriteAt in RenderGuardianDashboard。
- **新配置**：添加 config.json 字段；更新 GetConnectionString。
- **异步**：Task.Run(async () => { ... }, token)。

### 约束
- **平台**：Windows 优先。
- **依赖**：NuGet 包；版本兼容。
- **安全**：密码加密；P/Invoke 安全。
- **性能**：避免阻塞；内存谨慎。
- **异常处理**：不能有异常就退出；做好处理；日志记录。
- **功能影响**：新增功能不能影响已有功能，除非提出要求。
- **文档更新**：新增功能时更新文档，说明改了什么。
- **复用**：功能和函数尽可能复用，避免重复代码。

## 更新日志
- 2026-01-21：更新修改流程。
- 2026-01-21：添加新约束。