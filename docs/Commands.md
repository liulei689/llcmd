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

## 添加新命令
在 Program.Initialize() 添加 RegisterCommand。