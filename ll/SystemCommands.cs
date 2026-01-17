using System.Diagnostics;

namespace LL;

public static class SystemCommands
{
    public static void ShowTime(string[] args)
    {
        UI.PrintResult("当前时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss dddd"));
    }

    public static void SetTime(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            UI.PrintError("仅支持 Windows");
            return;
        }

        if (!ElevationCommands.IsAdministrator())
        {
            // on-demand elevation: run only this command in elevated child process
            if (ElevationCommands.RunElevatedCommand("settime", args))
            {
                UI.PrintInfo("已请求管理员权限(UAC)，已在新窗口执行 settime");
                return;
            }

            UI.PrintError("需要管理员权限");
            UI.PrintInfo("已取消管理员授权");
            return;
        }

        if (args.Length == 0)
        {
            UI.PrintError("请提供时间");
            UI.PrintInfo("用法: 19 2026-01-17 12:34:56");
            return;
        }

        var input = string.Join(' ', args).Trim();
        if (!DateTime.TryParse(input, out var dt))
        {
            UI.PrintError("时间格式不正确");
            return;
        }

        if (!TimeNativeMethods.SetLocalTime(dt))
        {
            UI.PrintError("修改系统时间失败");
            return;
        }

        UI.PrintSuccess($"已设置系统时间: {dt:yyyy-MM-dd HH:mm:ss}");
    }

    public static void ShowSysInfo(string[] args)
    {
        UI.PrintResult("操作系统", Environment.OSVersion.ToString());
        UI.PrintResult("计算机名", Environment.MachineName);
        UI.PrintResult("核心数", Environment.ProcessorCount.ToString());
        UI.PrintResult("运行环境", Environment.Version.ToString());
    }

internal static class TimeNativeMethods
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort wYear;
        public ushort wMonth;
        public ushort wDayOfWeek;
        public ushort wDay;
        public ushort wHour;
        public ushort wMinute;
        public ushort wSecond;
        public ushort wMilliseconds;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetLocalTime(ref SYSTEMTIME lpSystemTime);

    public static bool SetLocalTime(DateTime dt)
    {
        var st = new SYSTEMTIME
        {
            wYear = (ushort)dt.Year,
            wMonth = (ushort)dt.Month,
            wDay = (ushort)dt.Day,
            wDayOfWeek = (ushort)dt.DayOfWeek,
            wHour = (ushort)dt.Hour,
            wMinute = (ushort)dt.Minute,
            wSecond = (ushort)dt.Second,
            wMilliseconds = (ushort)dt.Millisecond
        };
        return SetLocalTime(ref st);
    }
}

    public static void CheckDirectorySize(string[] args)
    {
        string path = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        if (!Directory.Exists(path))
        {
            UI.PrintError($"路径不存在: {path}");
            return;
        }

        UI.PrintInfo($"正在分析: {path} ...");
        try
        {
            Stopwatch sw = Stopwatch.StartNew();
            long size = GetDirectorySize(new DirectoryInfo(path));
            sw.Stop();

            UI.PrintResult("目标路径", path);
            UI.PrintResult("总大小", Utils.FormatSize(size));
            UI.PrintResult("耗时", $"{sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            UI.PrintError(ex.Message);
        }
    }

    private static long GetDirectorySize(DirectoryInfo d)
    {
        long size = 0;
        try
        {
            FileInfo[] fis = d.GetFiles();
            foreach (FileInfo fi in fis) size += fi.Length;

            DirectoryInfo[] dis = d.GetDirectories();
            foreach (DirectoryInfo di in dis) size += GetDirectorySize(di);
        }
        catch { /* 忽略权限错误 */ }
        return size;
    }
}
