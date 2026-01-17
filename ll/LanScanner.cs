using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Collections.Concurrent;

namespace LL;

public static class LanScanner
{
    public static void Scan(string[] args)
    {
        // 用法:
        // lan                 -> 自动选择本机首个 IPv4 网段 /24
        // lan 192.168.1       -> 扫描 192.168.1.0/24
        // lan 192.168.1.0/24  -> 扫描指定 CIDR (仅支持 /24)
        // lan --fast          -> 并发更高

        string? prefix = null;
        bool scanAllLocal = args.Any(a => a.Equals("--all", StringComparison.OrdinalIgnoreCase));
        bool scan16 = args.Any(a => a.Equals("--16", StringComparison.OrdinalIgnoreCase));
        bool fast = args.Any(a => a.Equals("--fast", StringComparison.OrdinalIgnoreCase));

        foreach (var a in args)
        {
            if (a.StartsWith("--", StringComparison.OrdinalIgnoreCase)) continue;

            if (a.Contains('/'))
            {
                var parts = a.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && parts[1] == "24")
                {
                    var ipPart = parts[0];
                    if (TryGetPrefix(ipPart, out var p))
                        prefix = p;
                }
                else if (parts.Length == 2 && parts[1] == "16")
                {
                    // x.y.0.0/16
                    var ipPart = parts[0];
                    if (TryGetTwoOctetPrefix(ipPart, out var p2))
                    {
                        prefix = p2;
                        scan16 = true;
                    }
                }
            }
            else
            {
                if (TryGetPrefix(a, out var p))
                    prefix = p;
                else if (TryGetThreeOctetPrefix(a, out var p2))
                    prefix = p2;
                else if (TryGetTwoOctetPrefix(a, out var p3))
                {
                    prefix = p3;
                    scan16 = true;
                }
            }
        }

        var prefixes = new List<string>();
        if (prefix is not null)
        {
            prefixes.Add(prefix);
        }
        else
        {
            // Default: scan all local /24 prefixes
            prefixes.AddRange(GetLocalPrefixes());
        }

        if (prefixes.Count == 0)
        {
            UI.PrintError("无法识别本机 IPv4 网段，请手动指定，例如: lan 192.168.1 或 lan 192.168");
            return;
        }

        if (scanAllLocal)
        {
            prefixes = GetLocalPrefixes().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        int concurrency = fast ? 512 : 160;
        int timeoutMs = fast ? 250 : 700;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int alive = 0;
        long probed = 0;
        var aliveHosts = new ConcurrentBag<string>();

        if (scan16)
        {
            // /16 scan: prefix is "x.y" (two octets)
            var p = prefixes[0];
            if (!IsTwoOctetPrefix(p))
            {
                UI.PrintError("/16 扫描需要提供两段前缀，例如: lan 192.168 或 lan 192.168.0.0/16");
                return;
            }
            UI.PrintHeader($"局域网扫描: {p}.0.0/16");
            Scan16(p, concurrency, timeoutMs, aliveHosts, sw, ref alive, ref probed);
        }
        else
        {
            UI.PrintHeader($"局域网扫描: {string.Join(", ", prefixes.Select(p => p + ".0/24"))}");
            foreach (var p in prefixes.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Scan24(p, concurrency, timeoutMs, aliveHosts, sw, ref alive, ref probed);
            }
        }

        sw.Stop();
        Console.WriteLine();
        UI.PrintResult("扫描完成", $"已发现 {alive} 台（探测 {probed} 个地址，耗时 {sw.ElapsedMilliseconds}ms）");

        var ordered = aliveHosts
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => IPAddress.Parse(s))
            .OrderBy(ip => ip.GetAddressBytes(), ByteArrayComparer.Instance)
            .Select(ip => ip.ToString())
            .ToArray();

        if (ordered.Length > 0)
        {
            Console.WriteLine("\n在线设备列表:");
            foreach (var ip in ordered)
                Console.WriteLine($"- {ip}");
        }
    }

    private static void Scan24(string prefix, int concurrency, int timeoutMs, ConcurrentBag<string> aliveHosts, System.Diagnostics.Stopwatch sw, ref int alive, ref long probed)
    {
        if (!TryNormalize24Prefix(prefix, out var p)) return;

        // copy refs to local variables for safe capture in lambdas
        // then write back once after loop completes.
        int aliveLocal = alive;
        long probedLocal = probed;

        var opts = new ParallelOptions { MaxDegreeOfParallelism = concurrency };
        Parallel.For(1, 255, opts, i =>
        {
            var ip = $"{p}.{i}";
            Interlocked.Increment(ref probedLocal);

            if (PingOnce(ip, timeoutMs))
            {
                Interlocked.Increment(ref aliveLocal);
                aliveHosts.Add(ip);
                lock (Console.Out)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[在线] {ip}");
                    Console.ResetColor();
                }
            }
            else if (i % 64 == 0)
            {
                lock (Console.Out)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"进度: {p}.x  已探测: {Volatile.Read(ref probedLocal)}  已发现: {Volatile.Read(ref aliveLocal)}  用时: {sw.ElapsedMilliseconds}ms");
                    Console.ResetColor();
                }
            }
        });

        alive = aliveLocal;
        probed = probedLocal;
    }

    private static void Scan16(string twoOctet, int concurrency, int timeoutMs, ConcurrentBag<string> aliveHosts, System.Diagnostics.Stopwatch sw, ref int alive, ref long probed)
    {
        var opts = new ParallelOptions { MaxDegreeOfParallelism = concurrency };

        int aliveLocal = alive;
        long probedLocal = probed;

        // Scan x.y.1..254.1..254 (skip .0 and .255 for each octet)
        Parallel.For(1, 255, opts, third =>
        {
            var p = $"{twoOctet}.{third}";
            for (int fourth = 1; fourth <= 254; fourth++)
            {
                var ip = $"{p}.{fourth}";
                Interlocked.Increment(ref probedLocal);
                if (PingOnce(ip, timeoutMs))
                {
                    Interlocked.Increment(ref aliveLocal);
                    aliveHosts.Add(ip);
                    lock (Console.Out)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[在线] {ip}");
                        Console.ResetColor();
                    }
                }

                if (fourth % 128 == 0 && third % 8 == 0)
                {
                    lock (Console.Out)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"进度: {twoOctet}.{third}.x  已探测: {Volatile.Read(ref probedLocal)}  已发现: {Volatile.Read(ref aliveLocal)}  用时: {sw.ElapsedMilliseconds}ms");
                        Console.ResetColor();
                    }
                }
            }
        });

        alive = aliveLocal;
        probed = probedLocal;
    }

    private static bool PingOnce(string ip, int timeoutMs)
    {
        try
        {
            using var ping = new Ping();
            var reply = ping.Send(ip, timeoutMs);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private static List<string> GetLocalPrefixes()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var props = ni.GetIPProperties();
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var bytes = ua.Address.GetAddressBytes();
                    // 跳过 169.254.x.x
                    if (bytes[0] == 169 && bytes[1] == 254) continue;
                    list.Add($"{bytes[0]}.{bytes[1]}.{bytes[2]}");
                }
            }
        }
        catch { }
        return list;
    }

    private static bool TryGetThreeOctetPrefix(string input, out string prefix)
    {
        prefix = string.Empty;
        var parts = input.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3) return false;
        if (!byte.TryParse(parts[0], out var a)) return false;
        if (!byte.TryParse(parts[1], out var b)) return false;
        if (!byte.TryParse(parts[2], out var c)) return false;
        prefix = $"{a}.{b}.{c}";
        return true;
    }

    private static bool TryGetTwoOctetPrefix(string input, out string prefix)
    {
        prefix = string.Empty;
        // Accept x.y or x.y.0.0 (for /16)
        var parts = input.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;
        if (!byte.TryParse(parts[0], out var a)) return false;
        if (!byte.TryParse(parts[1], out var b)) return false;
        prefix = $"{a}.{b}";
        return true;
    }

    private static bool IsTwoOctetPrefix(string p) => p.Count(c => c == '.') == 1;

    private static bool TryNormalize24Prefix(string input, out string prefix)
    {
        prefix = string.Empty;
        if (TryGetThreeOctetPrefix(input, out var p))
        {
            prefix = p;
            return true;
        }
        if (TryGetPrefix(input, out var p2))
        {
            prefix = p2;
            return true;
        }
        return false;
    }

    private static bool TryGetPrefix(string input, out string prefix)
    {
        prefix = string.Empty;
        if (!IPAddress.TryParse(input, out var ip)) return false;
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = ip.GetAddressBytes();
        prefix = $"{bytes[0]}.{bytes[1]}.{bytes[2]}";
        return true;
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();

        public int Compare(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            int len = Math.Min(x.Length, y.Length);
            for (int i = 0; i < len; i++)
            {
                int c = x[i].CompareTo(y[i]);
                if (c != 0) return c;
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
