using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System;

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
                UI.PrintError("用法: server check | server ping <ip> <port> | server scan <ip> | server findopen <ip> [startPort endPort]");
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
                    await PingPort(ip, port);
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
            else if (subCommand == "findopen" && args.Length >= 2)
            {
                string ip = args[1];
                int startPort = 1;
                int endPort = 65535;
                if (args.Length >= 4 && int.TryParse(args[2], out int s) && int.TryParse(args[3], out int e))
                {
                    startPort = s;
                    endPort = e;
                }
                await FindOpenPorts(ip, startPort, endPort);
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

        private static async Task PingPort(string ip, int port)
        {
            Console.Write($"连接 {ip}:{port}...");
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(1000);
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == connectTask && client.Connected)
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
            UI.PrintHeader($"扫描 {ip} 的常见端口状态 (按 C 键取消)");
            int total = CommonPorts.Count;
            int completed = 0;
            using var cts = new CancellationTokenSource();
            TaskManager.Register($"端口扫描({ip})", cts);
            var token = cts.Token;

            // 启动监听C键线程
            var cancelThread = new Thread(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.C)
                    {
                        cts.Cancel();
                        break;
                    }
                    Thread.Sleep(100);
                }
            });
            cancelThread.IsBackground = true;
            cancelThread.Start();

            try
            {
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
                        var timeoutTask = Task.Delay(3000, token); // 3秒超时
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
            finally
            {
                TaskManager.Clear(cts);
                cancelThread.Join(200);
            }
        }

        // 高性能可用端口查找
        private static async Task FindOpenPorts(string ip, int startPort, int endPort)
        {
            UI.PrintHeader($"快速查找 {ip} 可用端口 [{startPort}-{endPort}] (多线程, 只输出开放, 按 C 键取消)");
            var openPorts = new List<int>();
            int total = endPort - startPort + 1;
            int completed = 0;
            int maxThreads = Math.Min(256, Environment.ProcessorCount * 32);
            var tasks = new List<Task>();
            var locker = new object();
            var cts = new CancellationTokenSource();
            TaskManager.Register($"端口查找({ip})", cts);
            var token = cts.Token;

            // 启动监听C键线程
            var cancelThread = new Thread(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.C)
                    {
                        cts.Cancel();
                        break;
                    }
                    Thread.Sleep(100);
                }
            });
            cancelThread.IsBackground = true;
            cancelThread.Start();

            // 启动进度更新任务
            var progressTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    Console.Write($"\r已扫描: {Volatile.Read(ref completed)}/{total} 开放: {openPorts.Count}");
                    await Task.Delay(500, token);
                }
            }, token);

            try
            {
                SemaphoreSlim throttler = new SemaphoreSlim(maxThreads);
                for (int port = startPort; port <= endPort; port++)
                {
                    if (token.IsCancellationRequested) break;
                    await throttler.WaitAsync(token);
                    int p = port;
                    var t = Task.Run(async () =>
                    {
                        try
                        {
                            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                            var result = socket.BeginConnect(ip, p, null, null);
                            var success = result.AsyncWaitHandle.WaitOne(3000);
                            if (success)
                            {
                                socket.EndConnect(result);
                                lock (locker) openPorts.Add(p);
                                Console.WriteLine($"\n[开放] {p}");
                            }
                            socket.Close();
                        }
                        catch { }
                        finally
                        {
                            Interlocked.Increment(ref completed);
                            throttler.Release();
                        }
                    }, token);
                    tasks.Add(t);
                }
                await Task.WhenAll(tasks);
                cts.Cancel(); // Stop progress task
                Console.WriteLine(); // 换行
                if (openPorts.Count == 0)
                {
                    UI.PrintInfo("未发现开放端口。");
                }
                else
                {
                    openPorts.Sort();
                    UI.PrintSuccess($"开放端口: {string.Join(", ", openPorts)}");
                }
            }
            catch (OperationCanceledException)
            {
                UI.PrintInfo("端口查找已取消。");
            }
            finally
            {
                TaskManager.Clear(cts);
                cancelThread.Join(200);
            }
        }
    }
}