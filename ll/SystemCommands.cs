using System.Diagnostics;

namespace LL;

public static class SystemCommands
{
    public static void ShowTime(string[] args)
    {
        UI.PrintResult("当前时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss dddd"));
    }

    public static void ShowSysInfo(string[] args)
    {
        UI.PrintResult("操作系统", Environment.OSVersion.ToString());
        UI.PrintResult("计算机名", Environment.MachineName);
        UI.PrintResult("核心数", Environment.ProcessorCount.ToString());
        UI.PrintResult("运行环境", Environment.Version.ToString());
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
