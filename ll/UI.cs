using System;

namespace LL;

public static class UI
{
    public static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
██╗      ██╗           ██████╗██╗     ██╗
██║      ██║          ██╔════╝██║     ██║
██║      ██║          ██║     ██║     ██║
██║      ██║          ██║     ██║     ██║
███████╗ ███████╗     ╚██████╗███████╗██║
╚══════╝ ╚══════╝      ╚═════╝╚══════╝╚═╝
");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("============================================================");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($" 会话 ID    : {Guid.NewGuid().ToString().Split('-')[0].ToUpper()} | 用户: {Environment.UserName}");
        Console.WriteLine($" 系统版本   : {Environment.OSVersion} | .NET: {Environment.Version}");
        Console.WriteLine($" 当前时间   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("============================================================");
        Console.ResetColor();
        Console.WriteLine();
    }

    public static void PrintHeader(string title)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[{title}]");
        Console.WriteLine(new string('-', 40));
        Console.ResetColor();
    }

    public static void PrintItem(string key, string desc)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($" {key.PadRight(12)}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"| {desc}");
        Console.ResetColor();
    }

    public static void PrintResult(string label, string value)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($" {label.PadRight(15)}: ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(value);
        Console.ResetColor();
    }

    public static void PrintInfo(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"[INFO] {msg}");
        Console.ResetColor();
    }

    public static void PrintSuccess(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[OK]   {msg}");
        Console.ResetColor();
    }

    public static void PrintError(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAIL] {msg}");
        Console.ResetColor();
    }
}
