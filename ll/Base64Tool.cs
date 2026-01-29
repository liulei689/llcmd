using System;
using System.IO;
using System.Text;
using LL;

namespace LL;

public static class Base64Tool
{
    public static void Handle(string[] args)
    {
        if (args.Length < 2 || args[0] == "help")
        {
            UI.PrintInfo("用法:");
            UI.PrintInfo("  base64 encode <text>");
            UI.PrintInfo("  base64 decode <base64_string>");
            UI.PrintInfo("  base64 encode --file <file_path>");
            UI.PrintInfo("  base64 decode --file <file_path> [--output <output_file>]");
            return;
        }

        string action = args[0].ToLower();
        string input = null;
        string filePath = null;
        string outputFile = null;

        if (args[1] == "--file")
        {
            if (args.Length >= 3) filePath = args[2];
            if (args.Length >= 5 && args[3] == "--output") outputFile = args[4];
        }
        else
        {
            input = string.Join(" ", args.Skip(1));
        }

        if (action == "encode")
        {
            if (filePath != null)
            {
                if (!File.Exists(filePath))
                {
                    UI.PrintError("文件不存在。");
                    return;
                }
                byte[] data = File.ReadAllBytes(filePath);
                string encoded = Convert.ToBase64String(data);
                UI.PrintSuccess($"Base64 编码: {TruncateString(encoded)}");
            }
            else if (input != null)
            {
                byte[] data = Encoding.UTF8.GetBytes(input);
                string encoded = Convert.ToBase64String(data);
                UI.PrintSuccess($"Base64 编码: {encoded}");
            }
        }
        else if (action == "decode")
        {
            if (filePath != null)
            {
                if (!File.Exists(filePath))
                {
                    UI.PrintError("文件不存在。");
                    return;
                }
                string base64 = File.ReadAllText(filePath).Replace("\r", "").Replace("\n", "");
                try
                {
                    byte[] data = Convert.FromBase64String(base64);
                    if (outputFile != null)
                    {
                        File.WriteAllBytes(outputFile, data);
                        UI.PrintSuccess($"解码数据已保存到 {outputFile}");
                    }
                    else
                    {
                        string decoded = Encoding.UTF8.GetString(data);
                        UI.PrintSuccess($"Base64 解码: {decoded}");
                    }
                }
                catch
                {
                    UI.PrintError("无效的 Base64 字符串。");
                }
            }
            else if (input != null)
            {
                try
                {
                    byte[] data = Convert.FromBase64String(input);
                    string decoded = Encoding.UTF8.GetString(data);
                    UI.PrintSuccess($"Base64 解码: {decoded}");
                }
                catch
                {
                    UI.PrintError("无效的 Base64 字符串。");
                }
            }
        }
        else
        {
            UI.PrintError("无效操作。使用 encode 或 decode。");
        }
    }

    private static string TruncateString(string str, int maxLength = 200)
    {
        if (str.Length <= maxLength) return str;
        return str.Substring(0, maxLength / 2) + " ... " + str.Substring(str.Length - maxLength / 2);
    }
}