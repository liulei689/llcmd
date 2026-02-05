# docs/Commands.md - LL CLI TOOL

## 命令列表
- **ls/list/help**: 显示指令清单。
- **gd**: 守护模式。
- **sd [time]**: 倒数关机。
- **idle [time]**: 空闲关机。
- **st**: 任务状态。
- **c**: 取消任务。
- **abort**: 中止关机。
- **myip**: 本机 IP。
- **netspeed [count]**: 网速测量。
- **lan [range]**: 局域网扫描。
- **ip**: 网络信息。
- **open [alias]**: 启动程序。
- **sys**: 系统信息。
- **time**: 系统时间。
- **size [path]**: 目录大小。
- **clr**: 清屏。
- **qr [text]**: 生成 QR。
- **admin [command]**: 提权执行。
- **settime [time]**: 设置时间。
- **ps [args]**: 进程列表。
- **kill [pid]**: 结束进程。
- **port [port]**: 端口检测。
- **curl [url]**: HTTP GET。
- **dns [domain]**: DNS 解析。
- **find [pattern]**: 查找文件。
- **watch [path]**: 监听目录。
- **clip [text]**: 剪贴板。
- **path**: PATH。
- **env**: 环境变量。
- **clean**: 清理。
- **hist**: 历史记录。
- **loc [path]**: 代码统计。
- **encv [file]**: 加密视频。
- **playv [file]**: 播放视频。
- **lsv**: 列出视频。
- **clrv**: 清理临时。
- **decv [file]**: 解密视频。
- **encf [file]**: 加密文件。
- **decf [file]**: 解密文件。
- **autostart [add/remove]**: 开机启动。
- **listen [channel]**: 监听 PG。
- **notify [channel] [message]**: 发送通知。
- **unlisten**: 停止监听。
- **encrypt [options]**: 加密。
- **decrypt [options]**: 解密。
- **task/dev/ctrl/set/netset/sound/disp/store/desk/tmp/recycle/snip/dnsflush/netfix**: 打开设置。
- **min**: 最小化窗口。
- **paste [text]**: 向当前活动窗口输入框发送文本，默认使用PresetText。
- **全局热键**：Ctrl+Shift+V 向当前活动窗口输入预定文本。

## wifi

用法:

- `wifi list`: 列出可用热点
- `wifi cur`: 显示当前连接/信号等接口信息
- `wifi connect <ssid>`: 连接指定 SSID（要求系统已保存该 Wi-Fi 配置文件）
- `wifi pwd <ssid>`: 查看已保存 Wi-Fi 密码（可能需要管理员权限）
- `wifi pwd-all`: 一键列出所有已保存 Wi-Fi 及其密码（读取失败会提示）

## 添加新命令
在 Program.Initialize() 添加 RegisterCommand。

## Git（精简）
项目内集成了一个精简的 `git` 子命令集合，方便在交互式界面快速查看历史和回退。注意这不是完整的 git 前端，只保留了常用、安全的操作。

- `git history [n]` 或 `git hist [n]`
  - 查看最近 n 条提交，默认 10。输出为：`<hash> <author> <relative-time> <subject>`。
  - 示例：`git history 5`

- `git info`
  - 显示仓库根目录、origin 远程地址（若存在）、当前分支与最近一次提交摘要。
  - 示例：`git info`

- `git rollback <commit> [--hard]` 或 `git rb <commit> [--hard]`
  - 不带 `--hard`：执行 `git checkout <commit>`（进入 detached HEAD，安全查看历史或临时回退）。
  - 带 `--hard`：执行 `git reset --hard <commit>`（危险，会丢失未提交更改，命令执行前要求交互确认）。

说明：
- 这些命令在内部调用本机安装的 `git`（会尝试查找常见安装路径或使用 `where git`）。
- 硬回退是破坏性操作，请在执行前确保已经备份或确认。

若需要完整的 git 子命令帮助，请在系统终端使用原始 `git` 命令（本工具保留调用原生命令的能力，但交互界面仅暴露上述精简集以避免误操作）。

- **template <type> [name]**: 生成项目模板 (支持短参数: c/console, w/webapi, lib/classlib 等, 调用 dotnet new, 自动重命名, 默认打开项目或文件夹)。
- **volume <action>**: 音量控制 (mute/unmute/up/down/set <level>)。
- **lottery**: 打开年会抽奖页面。
- **wallpaper <sub>**: 壁纸管理器 (set/random/online/bing/auto/source/folder/list/history/prev/mode)。
- **win <sub>**: 窗口管理器 (top/list/opacity/min/close/activate/info/move/resize)。

## wallpaper - 壁纸管理器

用法:

- `wallpaper set <path>`: 设置指定图片为桌面壁纸
- `wallpaper random` 或 `wallpaper r`: 从壁纸文件夹随机选择一张图片设为壁纸
- `wallpaper online [source]`: 设置在线随机壁纸 (可选指定源)
- `wallpaper bing [save]`: 下载并设置 Bing 每日美图
- `wallpaper auto <start|stop|status|interval>`: 自动定时切换壁纸
- `wallpaper source [name]`: 查看或设置壁纸源
- `wallpaper sources`: 列出所有可用壁纸源
- `wallpaper folder [path]`: 查看或设置壁纸文件夹路径
- `wallpaper list`: 列出壁纸文件夹中的所有图片
- `wallpaper history` 或 `wallpaper h`: 查看壁纸切换历史记录
- `wallpaper prev` 或 `wallpaper p`: 快速切回到上一张壁纸
- `wallpaper mode <style>`: 设置壁纸显示模式 (fill/fit/stretch/tile/center/span)

**可用壁纸源 (美女/二次元):**
- `btstu` - 搏天二次元萌图 (默认)
- `btstu2` - 搏天美女壁纸
- `paugram` - 保罗高清美女
- `dmoe` - 萌化二次元
- `mtyqx` - 墨天逸高清美女
- `zj` - 只因美女壁纸

示例:
```
wallpaper set C:\Pictures\wallpaper.jpg
wallpaper bing
wallpaper online              # 使用默认源
wallpaper online btstu2       # 使用搏天美女源
wallpaper auto start 10       # 每10分钟自动切换
wallpaper auto stop           # 停止自动切换
wallpaper source btstu2       # 设置默认源为美女
wallpaper mode fill
```

## win - 窗口管理器

用法:

- `win top [title]`: 置顶/取消置顶窗口 (默认当前窗口)
- `win list [filter]`: 列出所有可见窗口 (可带过滤关键字)
- `win opacity <0-255> [title]`: 设置窗口透明度 (0=完全透明, 255=不透明)
- `win min`: 最小化除当前窗口外的所有窗口
- `win close <title|index>`: 关闭指定窗口
- `win activate <title|index>`: 激活指定窗口
- `win info`: 显示当前活动窗口的详细信息
- `win move <x> <y> [title]`: 移动窗口到指定位置
- `win resize <w> <h> [title]`: 调整窗口大小

示例:
```
win top                    # 置顶当前窗口
win list                   # 列出所有窗口
win list chrome            # 列出包含 chrome 的窗口
win opacity 180            # 设置当前窗口透明度为 70%
win close 3                # 关闭列表中第 3 个窗口
win activate 微信          # 激活标题包含"微信"的窗口
```
