using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace LL;

public static class UtilityCommands
{
    public static void Execute(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "?" or "-h" or "--help")
        {
            PrintHelp();
            return;
        }

        var cmd = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        switch (cmd)
        {
            case "ps":
                ProcessTools.ListProcesses(rest);
                break;
            case "kill":
                ProcessTools.Kill(rest);
                break;
            case "port":
                NetTools.Port(rest);
                break;
            case "ip":
                NetTools.Ip();
                break;
            case "curl":
                NetTools.Curl(rest);
                break;
            case "dns":
                NetTools.Dns(rest);
                break;
            case "find":
                FileTools.Find(rest);
                break;
            case "watch":
                FileTools.Watch(rest);
                break;
            case "clip":
                ClipboardTools.Clip(rest);
                break;
            case "path":
                EnvTools.Path(rest);
                break;
            case "env":
                EnvTools.Env(rest);
                break;
            case "clean":
                FileTools.Clean(rest);
                break;
            default:
                UI.PrintError($"未知子命令: {cmd}");
                PrintHelp();
                break;
        }
    }

    private static void PrintHelp()
    {
        UI.PrintHeader("实用工具箱 util");
        UI.PrintInfo("用法: <子命令> [参数]");
        Console.WriteLine();
        UI.PrintResult("ps [关键词]", "列进程(按名称过滤)");
        UI.PrintResult("kill <pid|name>", "结束进程");
        UI.PrintResult("kill --port <port>", "结束监听该端口的进程");
        UI.PrintResult("port <端口>", "查看本机端口是否在监听/被占用(简版)");
        UI.PrintResult("ip", "显示网卡/IP/网关/DNS");
        UI.PrintResult("curl <url>", "HTTP GET(简版)");
        UI.PrintResult("dns <domain>", "DNS 解析");
        UI.PrintResult("find <关键字> [目录]", "递归查找文件名包含关键字");
        UI.PrintResult("watch <目录>", "监听目录变更(直到按 Ctrl+C)");
        UI.PrintResult("clip [文本]", "写入/读取剪贴板(Windows)");
        UI.PrintResult("path [filter]", "查看 PATH(可过滤)");
        UI.PrintResult("env [filter]", "查看环境变量(可过滤)");
        UI.PrintResult("clean temp", "清理临时目录(简版)");
    }

    private static class ProcessTools
    {
        public static void ListProcesses(string[] args)
        {
            string? filter = args.Length > 0 ? args[0] : null;
            var procs = System.Diagnostics.Process.GetProcesses();

            var list = procs
                .OrderByDescending(p => Safe(() => p.WorkingSet64, 0L))
                .Select(p => new
                {
                    Name = Safe(() => p.ProcessName, ""),
                    Id = Safe(() => p.Id, 0),
                    Ws = Safe(() => p.WorkingSet64, 0L),
                    Threads = Safe(() => p.Threads.Count, 0)
                });

            if (!string.IsNullOrWhiteSpace(filter))
                list = list.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

            UI.PrintHeader("进程列表");
            Console.WriteLine($"{"PID",6}  {"线程",4}  {"内存",10}  名称");
            foreach (var p in list.Take(50))
                Console.WriteLine($"{p.Id,6}  {p.Threads,4}  {Utils.FormatSize(p.Ws),10}  {p.Name}");
        }

        public static void Kill(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: kill <pid|name>");
                UI.PrintInfo("      kill --port <port>");
                return;
            }

            if ((args[0] is "--port" or "-p") && args.Length >= 2 && int.TryParse(args[1], out var port))
            {
                KillByPort(port);
                return;
            }

            var target = args[0];
            if (int.TryParse(target, out var pid))
            {
                try
                {
                    var p = System.Diagnostics.Process.GetProcessById(pid);
                    p.Kill(true);
                    UI.PrintSuccess($"已结束进程 PID={pid}");
                }
                catch (Exception ex)
                {
                    UI.PrintError($"结束失败: {ex.Message}");
                }
                return;
            }

            var matches = System.Diagnostics.Process.GetProcesses()
                .Where(p => Safe(() => p.ProcessName, "").Equals(target, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matches.Length == 0)
            {
                UI.PrintInfo("未找到进程。");
                return;
            }

            foreach (var p in matches)
            {
                try
                {
                    p.Kill(true);
                    UI.PrintSuccess($"已结束: {p.ProcessName} (PID={p.Id})");
                }
                catch (Exception ex)
                {
                    UI.PrintError($"结束失败: {p.ProcessName} (PID={p.Id}) - {ex.Message}");
                }
            }
        }

        private static void KillByPort(int port)
        {
            try
            {
                var text = RunAndCapture("netstat", "-ano -p tcp");
                var pids = new HashSet<int>();

                foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    // Example:
                    // TCP    0.0.0.0:80     0.0.0.0:0      LISTENING       1234
                    if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var parts = Regex.Split(line.Trim(), "\\s+");
                    if (parts.Length < 5)
                        continue;

                    var local = parts[1];
                    if (!TryParsePortFromEndpoint(local, out var localPort) || localPort != port)
                        continue;

                    if (int.TryParse(parts[^1], out var pid) && pid > 0)
                        pids.Add(pid);
                }

                if (pids.Count == 0)
                {
                    UI.PrintInfo($"未找到监听端口 {port} 的进程。");
                    return;
                }

                foreach (var pid in pids)
                {
                    try
                    {
                        var p = System.Diagnostics.Process.GetProcessById(pid);
                        p.Kill(true);
                        UI.PrintSuccess($"已结束端口 {port} 进程: {p.ProcessName} (PID={pid})");
                    }
                    catch (Exception ex)
                    {
                        UI.PrintError($"结束端口 {port} 进程失败 PID={pid}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                UI.PrintError($"按端口结束失败: {ex.Message}");
            }
        }

        private static bool TryParsePortFromEndpoint(string endpoint, out int port)
        {
            port = 0;

            // IPv6 in netstat can be like [::]:80
            endpoint = endpoint.Trim();
            if (endpoint.StartsWith("[", StringComparison.Ordinal) && endpoint.Contains("]:", StringComparison.Ordinal))
            {
                var idx = endpoint.LastIndexOf(":", StringComparison.Ordinal);
                if (idx > 0 && int.TryParse(endpoint[(idx + 1)..], out port))
                    return true;
                return false;
            }

            var last = endpoint.LastIndexOf(':');
            if (last <= 0) return false;
            return int.TryParse(endpoint[(last + 1)..], out port);
        }

        private static string RunAndCapture(string fileName, string arguments)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return "";
            var output = p.StandardOutput.ReadToEnd();
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit(5000);
            return string.IsNullOrWhiteSpace(output) ? err : output;
        }

        private static T Safe<T>(Func<T> fn, T fallback)
        {
            try { return fn(); } catch { return fallback; }
        }
    }

    private static class NetTools
    {
        public static void Ip()
        {
            UI.PrintHeader("网络信息");
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var props = ni.GetIPProperties();
                var ips = props.UnicastAddresses.Select(a => a.Address).Where(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6);
                var gw = props.GatewayAddresses.Select(g => g.Address).FirstOrDefault();
                var dns = props.DnsAddresses.Select(d => d.ToString()).Take(2);

                Console.WriteLine($"- {ni.Name} ({ni.OperationalStatus})");
                Console.WriteLine($"  IP : {string.Join(", ", ips)}");
                if (gw is not null) Console.WriteLine($"  GW : {gw}");
                Console.WriteLine($"  DNS: {string.Join(", ", dns)}");
            }
        }

        public static void Dns(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: dns <domain>");
                return;
            }

            var domain = args[0];
            try
            {
                var addrs = System.Net.Dns.GetHostAddresses(domain);
                UI.PrintHeader($"DNS: {domain}");
                foreach (var a in addrs)
                    Console.WriteLine(a);
            }
            catch (Exception ex)
            {
                UI.PrintError($"解析失败: {ex.Message}");
            }
        }

        public static void Curl(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: curl <url>");
                return;
            }

            var url = args[0];
            using var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
            http.Timeout = TimeSpan.FromSeconds(10);

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var resp = http.GetAsync(url).GetAwaiter().GetResult();
                var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                sw.Stop();

                UI.PrintHeader("HTTP GET");
                UI.PrintResult("状态", $"{(int)resp.StatusCode} {resp.ReasonPhrase}");
                UI.PrintResult("耗时", $"{sw.ElapsedMilliseconds} ms");
                UI.PrintResult("长度", body.Length.ToString());
                Console.WriteLine();
                Console.WriteLine(body.Length > 2000 ? body[..2000] : body);
            }
            catch (Exception ex)
            {
                UI.PrintError($"请求失败: {ex.Message}");
            }
        }

        public static void Port(string[] args)
        {
            if (args.Length == 0 || !int.TryParse(args[0], out var port))
            {
                UI.PrintError("用法: port <端口>");
                return;
            }

            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            bool listening = listeners.Any(ep => ep.Port == port);
            UI.PrintHeader("端口检测");
            UI.PrintResult("端口", port.ToString());
            UI.PrintResult("监听", listening ? "是" : "否");
        }
    }

    private static class FileTools
    {
        public static void Find(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: find <关键字> [目录]");
                return;
            }

            var keyword = args[0];
            var root = args.Length > 1 ? args[1] : Environment.CurrentDirectory;
            if (!Directory.Exists(root))
            {
                UI.PrintError($"目录不存在: {root}");
                return;
            }

            UI.PrintHeader($"查找文件名: {keyword}");
            int count = 0;
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(f);
                if (!name.Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;
                Console.WriteLine(f);
                if (++count >= 200) break;
            }
            UI.PrintInfo($"命中: {count}" );
        }

        public static void Watch(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: watch <目录>");
                return;
            }

            var path = args[0];
            if (!Directory.Exists(path))
            {
                UI.PrintError($"目录不存在: {path}");
                return;
            }

            UI.PrintHeader($"目录监听: {path}");
            using var fsw = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            fsw.Created += (_, e) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 新增: {e.FullPath}");
            fsw.Changed += (_, e) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 修改: {e.FullPath}");
            fsw.Deleted += (_, e) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 删除: {e.FullPath}");
            fsw.Renamed += (_, e) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 重命名: {e.OldFullPath} -> {e.FullPath}");

            UI.PrintInfo("运行中，按 Ctrl+C 结束。");
            var evt = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; evt.Set(); };
            evt.Wait();
        }

        public static void Clean(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: clean temp");
                return;
            }

            if (!args[0].Equals("temp", StringComparison.OrdinalIgnoreCase))
            {
                UI.PrintError("目前仅支持: clean temp");
                return;
            }

            var temp = Path.GetTempPath();
            UI.PrintHeader($"清理临时目录: {temp}");

            int deleted = 0;
            foreach (var f in Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(f);
                    deleted++;
                    if (deleted % 200 == 0)
                        Console.WriteLine($"已删除 {deleted} 个文件...");
                }
                catch { }
            }

            UI.PrintSuccess($"清理完成，删除文件数: {deleted}");
        }
    }

    private static class EnvTools
    {
        public static void Path(string[] args)
        {
            var filter = args.Length > 0 ? args[0] : null;
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            var parts = path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            UI.PrintHeader("PATH");
            foreach (var p in parts)
            {
                if (!string.IsNullOrWhiteSpace(filter) && !p.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                Console.WriteLine(p);
            }
        }

        public static void Env(string[] args)
        {
            var filter = args.Length > 0 ? args[0] : null;
            UI.PrintHeader("环境变量");

            foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
            {
                var k = kv.Key?.ToString() ?? "";
                var v = kv.Value?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(filter) && !k.Contains(filter, StringComparison.OrdinalIgnoreCase) && !v.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                Console.WriteLine($"{k}={v}");
            }
        }
    }

    private static class ClipboardTools
    {
        public static void Clip(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    // Read from clipboard via PowerShell (Windows)
                    var text = RunAndCapture("powershell", "-NoProfile -Command Get-Clipboard");
                    Console.WriteLine(text);
                    return;
                }

                var content = string.Join(' ', args);
                // Write to clipboard via 'clip' (Windows)
                RunWithStdin("cmd", "/c clip", content);
                UI.PrintSuccess("已写入剪贴板");
            }
            catch (Exception ex)
            {
                UI.PrintError($"剪贴板操作失败: {ex.Message}");
            }
        }

        private static string RunAndCapture(string fileName, string arguments)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return string.Empty;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            return output.TrimEnd();
        }

        private static void RunWithStdin(string fileName, string arguments, string stdin)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = Encoding.UTF8
            };

            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return;
            p.StandardInput.Write(stdin);
            p.StandardInput.Close();
            p.WaitForExit(3000);
        }
    }
}
