using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LL
{
    // 服务器检测相关命令
    internal static class ServerChecker
    {
        // 服务器IP列表，用户维护
        private static readonly List<(string Ip, string Name)> Servers = new()
        {
            ("8.8.8.8", "Google DNS"),
            ("1.1.1.1", "Cloudflare DNS"),
            ("127.0.0.1", "本地回环"),
            ("47.108.141.51", "阿里云服务器1"),
            ("47.108.141.52", "阿里云服务器2"),
            ("47.108.141.53", "阿里云服务器3"),
            // 添加更多服务器IP和名称
        };

        // 常见端口列表
        private static readonly List<(int Port, string Service)> CommonPorts = new()
        {
            (21, "FTP"),
            (22, "SSH"),
            (23, "Telnet"),
            (25, "SMTP"),
            (53, "DNS"),
            (80, "HTTP"),
            (110, "POP3"),
            (143, "IMAP"),
            (443, "HTTPS"),
            (3306, "MySQL"),
            (5432, "PostgreSQL"),
            (8080, "HTTP Alt"),
            (8443, "HTTPS Alt"),
        };

        public static async Task CheckServers(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: server check | server ping <ip> <port> | server scan <ip>");
                return;
            }

            string subCommand = args[0].ToLower();

            if (subCommand == "check")
            {
                CheckAllServers();
            }
            else if (subCommand == "ping" && args.Length >= 3)
            {
                string ip = args[1];
                if (int.TryParse(args[2], out int port))
                {
                    PingPort(ip, port);
                }
                else
                {
                    UI.PrintError("端口必须是数字。");
                }
            }
            else if (subCommand == "scan" && args.Length >= 2)
            {
                string ip = args[1];
                await ScanPorts(ip);
            }
            else
            {
                UI.PrintError("无效参数。用法: server check | server ping <ip> <port> | server scan <ip>");
            }
        }

        private static void CheckAllServers()
        {
            UI.PrintHeader("批量检测服务器在线状态");
            int total = Servers.Count;
            int completed = 0;
            foreach (var (ip, name) in Servers)
            {
                completed++;
                bool detecting = true;
                string dots = ".";
                var animationTask = Task.Run(async () =>
                {
                    while (detecting)
                    {
                        Console.Write($"\r[{completed}/{total}] 检测 {name} ({ip}){dots}");
                        dots = dots.Length < 3 ? dots + "." : ".";
                        await Task.Delay(300);
                    }
                });

                bool isOnline = PingServer(ip);
                detecting = false;
                string status = isOnline ? "在线" : "离线";
                string color = isOnline ? "\u001b[32m" : "\u001b[31m";
                Console.Write($"\r{color}[{completed}/{total}] 检测 {name} ({ip}) {status}\u001b[0m\n");
                animationTask.Wait(); // 确保动画停止
            }
            UI.PrintSuccess("所有服务器检测完成。");
        }

        private static bool PingServer(string ip)
        {
            try
            {
                using var ping = new Ping();
                var reply = ping.Send(ip, 2000); // 2秒超时
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        private static void PingPort(string ip, int port)
        {
            Console.Write($"连接 {ip}:{port}...");
            try
            {
                using var client = new TcpClient();
                var result = client.BeginConnect(ip, port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(500); // 5秒超时
                client.EndConnect(result);
                if (success)
                {
                    Console.WriteLine(" \u001b[32m成功\u001b[0m");
                }
                else
                {
                    Console.WriteLine(" \u001b[31m失败或超时\u001b[0m");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($" \u001b[31m错误: {ex.Message}\u001b[0m");
            }
        }

        private static async Task ScanPorts(string ip)
        {
            UI.PrintHeader($"扫描 {ip} 的常见端口状态");
            int total = CommonPorts.Count;
            int completed = 0;
            using var cts = new CancellationTokenSource();
            var token = cts.Token;

            foreach (var (port, service) in CommonPorts)
            {
                if (token.IsCancellationRequested)
                {
                    UI.PrintInfo("端口扫描已取消。");
                    return;
                }

                completed++;
                bool detecting = true;
                string dots = ".";
                var animationTask = Task.Run(async () =>
                {
                    while (detecting && !token.IsCancellationRequested)
                    {
                        Console.Write($"\r[{completed}/{total}] 扫描 {service} ({port}){dots}");
                        dots = dots.Length < 3 ? dots + "." : ".";
                        await Task.Delay(200);
                    }
                }, token);

                try
                {
                    using var client = new TcpClient();
                    var connectTask = client.ConnectAsync(ip, port);
                    var timeoutTask = Task.Delay(1000, token); // 1秒超时
                    var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                    detecting = false;
                    if (completedTask == connectTask)
                    {
                        string color = "\u001b[32m";
                        Console.Write($"\r{color}[{completed}/{total}] {service} ({port}) 开放\u001b[0m\n");
                    }
                    else
                    {
                        string color = "\u001b[31m";
                        Console.Write($"\r{color}[{completed}/{total}] {service} ({port}) 关闭\u001b[0m\n");
                    }
                }
                catch
                {
                    detecting = false;
                    Console.Write($"\r\u001b[31m[{completed}/{total}] {service} ({port}) 错误\u001b[0m\n");
                }

                await animationTask;
            }
            UI.PrintSuccess("端口扫描完成。");
        }
    }
}