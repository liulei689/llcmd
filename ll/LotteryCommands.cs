using System;
using System.Diagnostics;
using System.IO;

namespace LL;

public static class LotteryCommands
{
    public static void Run(string[] args)
    {
        var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "cj.html");
        if (!File.Exists(htmlPath))
        {
            UI.PrintError($"未找到抽奖页面: {htmlPath}");
            return;
        }
        OpenInBrowser(htmlPath);
        UI.PrintSuccess("抽奖页面已打开，请在浏览器中查看");
    }

    private static void OpenInBrowser(string htmlPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = htmlPath,
            UseShellExecute = true
        });
    }
}