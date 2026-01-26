using System;
using System.Diagnostics;
using System.Linq;
using LL;

namespace LL
{
    internal static class EventLogViewer
    {
        public static void ViewEventLog(string[] args)
        {
            if (args.Length > 0 && args[0].ToLower() == "open")
            {
                try
                {
                    Process.Start(new ProcessStartInfo("eventvwr.exe") { UseShellExecute = true });
                    UI.PrintSuccess("已打开 Windows 事件查看器。");
                }
                catch (Exception ex)
                {
                    UI.PrintError($"打开事件查看器失败: {ex.Message}");
                }
                return;
            }

            string filter = args.Length > 0 ? args[0].ToLower() : "";
            int maxEntries = 50; // 增加到 50 条

            try
            {
                UI.PrintHeader("Windows 事件日志查看器");

                // 获取系统日志
                using (EventLog eventLog = new EventLog("System"))
                {
                    var entries = eventLog.Entries.Cast<EventLogEntry>()
                        .Take(maxEntries) // 直接取前 50 条（最新的）
                        .Where(e => string.IsNullOrEmpty(filter) || e.EntryType.ToString().ToLower().Contains(filter));

                    if (!entries.Any())
                    {
                        UI.PrintInfo("未找到匹配的事件日志条目。");
                        return;
                    }

                    UI.PrintInfo("时间\t级别\t来源\t消息");
                    UI.PrintInfo("----\t----\t----\t----");

                    foreach (var entry in entries)
                    {
                        string level = entry.EntryType.ToString();
                        string color = level switch
                        {
                            "Error" => "\u001b[31m", // 红色
                            "Warning" => "\u001b[33m", // 黄色
                            "Information" => "\u001b[32m", // 绿色
                            _ => "\u001b[37m" // 白色
                        };

                        string time = entry.TimeGenerated.ToString("yyyy-MM-dd HH:mm:ss");
                        string source = entry.Source.Length > 20 ? entry.Source.Substring(0, 17) + "..." : entry.Source;
                        string message = entry.Message.Length > 50 ? entry.Message.Substring(0, 47) + "..." : entry.Message;

                        Console.WriteLine($"{color}{time}\t{level}\t{source}\t{message}\u001b[0m");
                    }
                }
            }
            catch (Exception ex)
            {
                UI.PrintError($"读取事件日志失败: {ex.Message}");
            }
        }
    }
}