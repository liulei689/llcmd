using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace LL;

public static class WifiCommands
{
    public static void Handle(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            PrintHelp();
            return;
        }

        string sub = args[0].ToLowerInvariant();
        string[] rest = args.Skip(1).ToArray();

        try
        {
            switch (sub)
            {
                case "list":
                    ListNetworks();
                    break;
                case "cur":
                    ShowCurrent();
                    break;
                case "connect":
                    Connect(rest);
                    break;
                case "pwd":
                    ShowPassword(rest);
                    break;
                case "pwd-all":
                    ShowAllPasswords();
                    break;
                default:
                    UI.PrintError($"未知子命令: {sub}");
                    PrintHelp();
                    break;
            }
        }
        catch (Exception ex)
        {
            UI.PrintError($"wifi 执行失败: {ex.Message}");
        }
    }

    private static void PrintHelp()
    {
        UI.PrintHeader("wifi - Wi-Fi 工具");
        UI.PrintInfo("用法: wifi <sub> [args]");
        Console.WriteLine();
        UI.PrintResult("wifi list", "列出可用热点 (netsh wlan show networks)");
        UI.PrintResult("wifi cur", "显示当前连接/信号");
        UI.PrintResult("wifi connect <ssid>", "连接指定 SSID (要求系统已有该配置文件)");
        UI.PrintResult("wifi pwd <ssid>", "查看已保存 Wi-Fi 密码");
        UI.PrintResult("wifi pwd-all", "一键列出所有已保存 Wi-Fi 及密码");
        UI.PrintInfo("提示: 依赖 netsh，Windows 可用。查看密码可能需要管理员权限。");
    }

    private static void ListNetworks()
    {
        UI.PrintHeader("Wi-Fi 热点列表");
        var text = RunNetsh("wlan show networks mode=bssid");
        PrintTextTruncated(text);
    }

    private static void ShowCurrent()
    {
        UI.PrintHeader("Wi-Fi 当前连接");
        var text = RunNetsh("wlan show interfaces");
        PrintTextTruncated(text);
    }

    private static void Connect(string[] args)
    {
        if (args.Length == 0)
        {
            UI.PrintError("用法: wifi connect <ssid>");
            return;
        }

        string ssid = string.Join(' ', args).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(ssid))
        {
            UI.PrintError("SSID 不能为空。");
            return;
        }

        UI.PrintHeader("Wi-Fi 连接");
        UI.PrintResult("SSID", ssid);

        // Note: netsh connect requires existing profile for this SSID.
        var text = RunNetsh($"wlan connect name=\"{ssid}\" ssid=\"{ssid}\"");
        PrintTextTruncated(text);
    }

    private static void ShowPassword(string[] args)
    {
        if (args.Length == 0)
        {
            UI.PrintError("用法: wifi pwd <ssid>");
            return;
        }

        string ssid = string.Join(' ', args).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(ssid))
        {
            UI.PrintError("SSID 不能为空。");
            return;
        }

        UI.PrintHeader("Wi-Fi 密码");
        UI.PrintResult("SSID", ssid);

        // netsh output contains: Key Content            : xxxx
        string output = RunNetsh($"wlan show profile name=\"{ssid}\" key=clear");
        string? pwd = TryParseKeyContent(output);

        if (pwd is null)
        {
            UI.PrintError("未找到密码 (可能未保存该 Wi-Fi，或需要管理员权限)。");
            PrintTextTruncated(output);
            return;
        }

        UI.PrintSuccess(pwd);
    }

    private static void ShowAllPasswords()
    {
        UI.PrintHeader("Wi-Fi 密码 (全部)");

        string listOutput = RunNetsh("wlan show profiles");
        var ssids = ParseProfiles(listOutput);

        if (ssids.Count == 0)
        {
            UI.PrintInfo("未找到已保存的 Wi-Fi 配置。");
            return;
        }

        UI.PrintResult("配置数", ssids.Count.ToString("n0"));
        Console.WriteLine();

        int ok = 0;
        int fail = 0;

        foreach (var ssid in ssids)
        {
            try
            {
                string output = RunNetsh($"wlan show profile name=\"{ssid}\" key=clear");
                string? pwd = TryParseKeyContent(output);

                if (pwd is null)
                {
                    UI.PrintResult(ssid, "(无密码/未能读取)");
                    fail++;
                }
                else
                {
                    UI.PrintResult(ssid, pwd);
                    ok++;
                }
            }
            catch
            {
                UI.PrintResult(ssid, "(读取失败)");
                fail++;
            }
        }

        Console.WriteLine();
        UI.PrintInfo($"完成: 成功 {ok:n0}, 失败 {fail:n0}");
        if (fail > 0)
            UI.PrintInfo("提示: 失败通常是权限不足或该 profile 为企业/无密码类型。");
    }

    private static List<string> ParseProfiles(string text)
    {
        var list = new List<string>();
        // CN system: "所有用户配置文件"; EN: "All User Profile"
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;

            var left = line[..idx].Trim();
            if (!left.Contains("Profile", StringComparison.OrdinalIgnoreCase) && !left.Contains("配置文件", StringComparison.OrdinalIgnoreCase))
                continue;

            var name = line[(idx + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            list.Add(name);
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? TryParseKeyContent(string text)
    {
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            // CN: "关键内容"; EN: "Key Content"
            if (!line.Contains("Key Content", StringComparison.OrdinalIgnoreCase) && !line.Contains("关键内容", StringComparison.OrdinalIgnoreCase))
                continue;

            int idx = line.IndexOf(':');
            if (idx < 0) continue;
            var val = line[(idx + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(val)) return null;
            return val;
        }

        return null;
    }

    private static string RunNetsh(string arguments)
    {
        var psi = new ProcessStartInfo("netsh", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var p = Process.Start(psi);
        if (p is null) return string.Empty;

        string output = p.StandardOutput.ReadToEnd();
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit(8_000);

        if (!string.IsNullOrWhiteSpace(err) && string.IsNullOrWhiteSpace(output))
            return err.TrimEnd();

        return output.TrimEnd();
    }

    private static void PrintTextTruncated(string text)
    {
        const int max = 4000;
        if (string.IsNullOrEmpty(text))
        {
            UI.PrintInfo("(无输出)");
            return;
        }

        if (text.Length <= max)
        {
            Console.WriteLine(text);
            return;
        }

        int head = 1800;
        int tail = 1800;
        Console.WriteLine(text[..head]);
        Console.WriteLine();
        UI.PrintInfo("...已省略... ");
        Console.WriteLine();
        Console.WriteLine(text[^tail..]);
    }
}
