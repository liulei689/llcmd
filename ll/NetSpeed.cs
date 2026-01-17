using System.Net.NetworkInformation;

namespace LL;

public static class NetSpeed
{
    public static void Measure(string[] args)
    {
        // 用法:
        // netspeed                 -> 实时刷新(默认 10 秒，可按任意键退出)
        // netspeed 30              -> 实时刷新 30 秒
        // netspeed --list          -> 实时 + 列出每张网卡
        // netspeed --once 2        -> 只采样一次(2 秒)

        bool list = args.Any(a => a.Equals("--list", StringComparison.OrdinalIgnoreCase));
        bool once = args.Any(a => a.Equals("--once", StringComparison.OrdinalIgnoreCase));

        int seconds = once ? 1 : 10;
        foreach (var a in args)
        {
            if (a.StartsWith("--", StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(a, out var s) && s >= 1 && s <= 3600) seconds = s;
        }

        var nics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToArray();

        if (nics.Length == 0)
        {
            UI.PrintError("未找到可用网卡");
            return;
        }

        // Default: pick the most likely "primary" NIC (has gateway, not virtual/tunnel),
        // otherwise fall back to the NIC with most traffic.
        nics = SelectPrimaryNic(nics);

        if (once)
        {
            PrintOnce(nics, seconds, list);
            return;
        }
        Live(nics, seconds, list);
    }

    private static NetworkInterface[] SelectPrimaryNic(NetworkInterface[] nics)
    {
        static bool LooksVirtual(NetworkInterface ni)
        {
            var n = (ni.Name + " " + ni.Description).ToLowerInvariant();
            return n.Contains("virtual") || n.Contains("vmware") || n.Contains("hyper-v") || n.Contains("tunnel") || n.Contains("loopback") || n.Contains("tap") || n.Contains("wintun") || n.Contains("wireguard");
        }

        try
        {
            var withGw = nics
                .Select(ni => new
                {
                    ni,
                    gw = ni.GetIPProperties().GatewayAddresses.Any(g => g.Address is not null && !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any))
                })
                .Where(x => x.gw)
                .Select(x => x.ni)
                .ToArray();

            var candidates = withGw.Length > 0 ? withGw : nics;
            var filtered = candidates.Where(ni => !LooksVirtual(ni)).ToArray();
            if (filtered.Length > 0)
                return filtered;
        }
        catch { }

        return nics;
    }

    private static void PrintOnce(NetworkInterface[] nics, int seconds, bool list)
    {
        var before = Snapshot(nics);
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        var after = Snapshot(nics);
        var dt = TimeSpan.FromSeconds(seconds);
        var rows = CalcRows(nics, before, after, dt);

        double sumRx = rows.Sum(r => r.RxBps);
        double sumTx = rows.Sum(r => r.TxBps);

        UI.PrintHeader($"当前网速（采样 {seconds}s）");
        UI.PrintResult("下行", $"{Utils.FormatSize((long)sumRx)}/秒");
        UI.PrintResult("上行", $"{Utils.FormatSize((long)sumTx)}/秒");

        if (list)
            PrintList(rows);
    }

    private static void Live(NetworkInterface[] nics, int seconds, bool list)
    {
        UI.PrintHeader($"当前网速（实时 {seconds}s，按任意键退出）");

        bool oldCursor = Console.CursorVisible;
        Console.CursorVisible = false;
        int startTop = Console.CursorTop;

        try
        {
            var before = Snapshot(nics);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastMs = 0;

            for (int tick = 0; tick < seconds; tick++)
            {
                if (Console.KeyAvailable)
                {
                    _ = Console.ReadKey(true);
                    break;
                }

                Thread.Sleep(1000);
                var after = Snapshot(nics);
                var nowMs = sw.ElapsedMilliseconds;
                // Use actual elapsed time between reads (sleep is not exact)
                var dt = TimeSpan.FromMilliseconds(Math.Max(1, nowMs - lastMs));
                lastMs = nowMs;

                var rows = CalcRows(nics, before, after, dt);
                before = after;

                double sumRx = rows.Sum(r => r.RxBps);
                double sumTx = rows.Sum(r => r.TxBps);

                // rewrite in-place
                Console.SetCursorPosition(0, startTop);
                Console.Write(new string(' ', Math.Max(0, Console.WindowWidth - 1)));
                Console.SetCursorPosition(0, startTop);
                Console.Write($"下行 {Utils.FormatSize((long)sumRx)}/秒   上行 {Utils.FormatSize((long)sumTx)}/秒   已运行 {TimeSpan.FromMilliseconds(nowMs):mm\\:ss}");

                if (list)
                {
                    Console.SetCursorPosition(0, startTop + 1);
                    ClearLines(startTop + 1, 22);
                    Console.SetCursorPosition(0, startTop + 1);
                    PrintList(rows);
                }
            }
        }
        finally
        {
            Console.CursorVisible = oldCursor;
            Console.WriteLine();
        }
    }

    private static List<Row> CalcRows(NetworkInterface[] nics, Dictionary<string, (long Rx, long Tx)> before, Dictionary<string, (long Rx, long Tx)> after, TimeSpan dt)
    {
        var rows = new List<Row>();
        foreach (var ni in nics)
        {
            if (!before.TryGetValue(ni.Id, out var b)) continue;
            if (!after.TryGetValue(ni.Id, out var a)) continue;

            var rxBps = Math.Max(0, (a.Rx - b.Rx) / dt.TotalSeconds);
            var txBps = Math.Max(0, (a.Tx - b.Tx) / dt.TotalSeconds);
            rows.Add(new Row(ni.Name, rxBps, txBps));
        }
        return rows;
    }

    private static void PrintList(List<Row> rows)
    {
        Console.WriteLine(string.Format("{0,-28}  {1,12}  {2,12}", "网卡", "下行", "上行"));
        foreach (var r in rows.OrderByDescending(r => r.RxBps + r.TxBps).Take(20))
        {
            Console.WriteLine($"{Utils.Truncate(r.Name, 28),-28}  {Utils.FormatSize((long)r.RxBps),12}/s  {Utils.FormatSize((long)r.TxBps),12}/s");
        }
    }

    private static void ClearLines(int top, int count)
    {
        int w = Math.Max(0, Console.WindowWidth - 1);
        for (int i = 0; i < count && top + i < Console.BufferHeight; i++)
        {
            Console.SetCursorPosition(0, top + i);
            Console.Write(new string(' ', w));
        }
    }

    private static Dictionary<string, (long Rx, long Tx)> Snapshot(NetworkInterface[] nics)
    {
        var map = new Dictionary<string, (long Rx, long Tx)>(StringComparer.OrdinalIgnoreCase);
        foreach (var ni in nics)
        {
            try
            {
                // Include IPv4 + IPv6 (more accurate on modern Windows)
                var stats = ni.GetIPStatistics();
                map[ni.Id] = (stats.BytesReceived, stats.BytesSent);
            }
            catch
            {
            }
        }
        return map;
    }

    private readonly record struct Row(string Name, double RxBps, double TxBps);
}
