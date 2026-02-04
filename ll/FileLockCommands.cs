using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LL;

public static class FileLockCommands
{
    public static void Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            PrintHelp();
            return;
        }

        string target = args[0].Trim('"');
        bool recursive = args.Any(a => a is "-r" or "--recursive");
        bool unlock = args.Any(a => a is "--unlock" or "--kill" or "-k");
        bool force = args.Any(a => a is "-y" or "--yes");

        if (!File.Exists(target) && !Directory.Exists(target))
        {
            UI.PrintError($"文件/目录不存在: {target}");
            return;
        }

        var files = ExpandTargets(target, recursive);
        if (files.Count == 0)
        {
            UI.PrintError("未找到文件。");
            return;
        }

        UI.PrintHeader("文件占用检测");
        UI.PrintResult("目标", Path.GetFullPath(target));
        UI.PrintResult("文件数", files.Count.ToString("n0"));
        UI.PrintResult("递归", recursive ? "是" : "否");
        UI.PrintResult("解除占用", unlock ? "是(尝试结束占用进程)" : "否");
        Console.WriteLine();

        int locked = 0;
        int ok = 0;

        foreach (var f in files)
        {
            var result = CheckLock(f);
            if (!result.IsLocked)
            {
                ok++;
                continue;
            }

            locked++;
            UI.PrintError($"被占用: {f}");

            if (!string.IsNullOrWhiteSpace(result.Details))
                UI.PrintInfo(result.Details!);

            if (!unlock)
                continue;

            if (result.Pids.Count == 0)
            {
                UI.PrintInfo("未能定位占用进程(PID)。可能原因: 非 Windows/权限不足/占用来自内核驱动/文件不存在等。");
                continue;
            }

            if (!force)
            {
                Console.Write($"结束进程以解除占用? PIDs={string.Join(",", result.Pids)} (y/N): ");
                var s = Console.ReadLine();
                if (!string.Equals(s, "y", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            foreach (var pid in result.Pids.Distinct())
            {
                TryKill(pid);
            }
        }

        Console.WriteLine();
        UI.PrintSuccess($"完成: 正常 {ok:n0}, 被占用 {locked:n0}");
        if (unlock)
        {
            UI.PrintInfo("提示: 解除占用通过结束进程实现，可能导致未保存数据丢失。建议先确认后使用 --unlock -y。");
        }
    }

    private static void PrintHelp()
    {
        UI.PrintInfo(
            "用法: lock <file|dir> [-r] [--unlock|-k] [-y]\n" +
            "说明: 检测文件是否被占用；可选尝试解除(结束占用进程)。\n" +
            "  lock a.mp4                 检测单文件\n" +
            "  lock D:/videos             批量检测目录(不递归)\n" +
            "  lock D:/videos -r          递归检测\n" +
            "  lock D:/videos -r --unlock -y  自动结束占用进程(危险)\n" +
            "实现说明: Windows 下优先用 Sysinternals handle.exe 定位占用 PID；缺少 handle.exe 时只能做“能否打开文件”的占用判断。\n" +
            "准备: 将 handle.exe 放到 tools/bin/handle.exe(可选)。"
        );
    }

    private static List<string> ExpandTargets(string target, bool recursive)
    {
        var list = new List<string>();
        if (File.Exists(target))
        {
            list.Add(target);
            return list;
        }

        if (Directory.Exists(target))
        {
            var opt = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true };
            foreach (var f in Directory.EnumerateFiles(target, "*.*", opt))
                list.Add(f);
        }

        return list;
    }

    private sealed record LockCheckResult(bool IsLocked, string? Details, List<int> Pids);

    private static LockCheckResult CheckLock(string file)
    {
        // 1) 基础判断：能否独占打开
        bool locked;
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            locked = false;
        }
        catch (IOException)
        {
            locked = true;
        }
        catch (UnauthorizedAccessException)
        {
            // 也可能是权限问题，这里也当成“不可访问”提示
            return new LockCheckResult(true, "无权限访问(可能是权限问题，不一定是占用)。", new List<int>());
        }

        // 2) 尝试定位 PID（可选：handle.exe）
        var pids = new List<int>();
        string? details = null;

        var handleExe = Path.Combine(AppContext.BaseDirectory, "tools", "bin", "handle.exe");
        if (locked && File.Exists(handleExe))
        {
            var outText = RunHandle(handleExe, file);
            if (!string.IsNullOrWhiteSpace(outText))
            {
                // handle 输出格式会随版本变化，这里做宽松解析：抓取 "pid: NNN" 或 "pid NNN"
                foreach (var pid in ExtractPids(outText))
                    pids.Add(pid);

                details = SummarizeHandleOutput(outText);
            }
        }

        // 3) Windows 兜底：Restart Manager API 定位锁定进程（不需要额外工具）
        if (locked && pids.Count == 0 && OperatingSystem.IsWindows())
        {
            try
            {
                var rm = GetLockingProcessesRestartManager(file);
                if (rm.Count > 0)
                {
                    pids.AddRange(rm.Select(x => x.Pid));
                    var top = string.Join(Environment.NewLine, rm.Take(10).Select(x => $"{x.ProcessName} (PID {x.Pid})"));
                    details = string.IsNullOrWhiteSpace(details)
                        ? "Restart Manager 检测到占用进程:\n" + top
                        : details + "\n\nRestart Manager 检测到占用进程:\n" + top;
                }
            }
            catch
            {
                // ignore
            }
        }

        return new LockCheckResult(locked, details, pids);
    }

    private sealed record ProcInfo(int Pid, string ProcessName);

    [SupportedOSPlatform("windows")]
    private static List<ProcInfo> GetLockingProcessesRestartManager(string file)
    {
        // https://learn.microsoft.com/windows/win32/rstmgr/restart-manager-portal
        // 使用 Restart Manager API 枚举锁定指定文件的进程。
        var result = new List<ProcInfo>();
        uint handle;
        string sessionKey = Guid.NewGuid().ToString("N");
        int err = RmStartSession(out handle, 0, sessionKey);
        if (err != 0) return result;

        try
        {
            string[] resources = [file];
            err = RmRegisterResources(handle, (uint)resources.Length, resources, 0, null, 0, null);
            if (err != 0) return result;

            uint procInfoNeeded = 0;
            uint procInfo = 0;
            uint rebootReasons = 0;

            // 第一次调用获取需要多少条目
            err = RmGetList(handle, out procInfoNeeded, ref procInfo, null, ref rebootReasons);
            if (err == ERROR_MORE_DATA)
            {
                procInfo = procInfoNeeded;
                var infos = new RM_PROCESS_INFO[procInfo];
                err = RmGetList(handle, out procInfoNeeded, ref procInfo, infos, ref rebootReasons);
                if (err == 0)
                {
                    for (int i = 0; i < procInfo; i++)
                    {
                        int pid = infos[i].Process.dwProcessId;
                        if (pid <= 0) continue;
                        string name = infos[i].strAppName;
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            try { name = Process.GetProcessById(pid).ProcessName; } catch { name = "(unknown)"; }
                        }
                        result.Add(new ProcInfo(pid, name));
                    }
                }
            }
            else if (err == 0)
            {
                // 没有锁定者
            }

            return result;
        }
        finally
        {
            RmEndSession(handle);
        }
    }

    private const int ERROR_MORE_DATA = 234;

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[]? rgsFilenames, uint nApplications, [In] RM_UNIQUE_PROCESS[]? rgApplications, uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo, [Out] RM_PROCESS_INFO[]? rgAffectedApps, ref uint lpdwRebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;

        public uint ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    private static string RunHandle(string exe, string file)
    {
        try
        {
            // -accepteula: 避免首次弹 EULA 卡住
            // -nobanner: 减少噪声
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"-accepteula -nobanner \"{file}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null) return "";
            var o = p.StandardOutput.ReadToEnd();
            var e = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (o + "\n" + e).Trim();
        }
        catch
        {
            return "";
        }
    }

    private static List<int> ExtractPids(string text)
    {
        // 简单扫描 "pid" 后的数字。避免 iterator/yield 与 Span 造成的编译限制。
        var pids = new List<int>();
        for (int i = 0; i < text.Length - 4; i++)
        {
            char c0 = text[i];
            if (c0 != 'p' && c0 != 'P') continue;
            char c1 = text[i + 1];
            char c2 = text[i + 2];
            if ((c1 != 'i' && c1 != 'I') || (c2 != 'd' && c2 != 'D')) continue;

            int j = i + 3;
            while (j < text.Length && (text[j] == ' ' || text[j] == ':' || text[j] == '\t')) j++;
            int start = j;
            while (j < text.Length && char.IsDigit(text[j])) j++;
            if (j > start && int.TryParse(text.AsSpan(start, j - start), out var pid))
                pids.Add(pid);
        }
        return pids;
    }

    private static string SummarizeHandleOutput(string text)
    {
        // 保留前若干行，避免刷屏
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine, lines.Take(10));
    }

    private static void TryKill(int pid)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            UI.PrintInfo($"尝试结束进程: {p.ProcessName} (PID {pid})");
            p.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            UI.PrintError($"无法结束 PID {pid}: {ex.Message}");
        }
    }
}
