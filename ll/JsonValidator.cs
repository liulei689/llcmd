using System;
using System.IO;
using System.Text.Json;
using LL;

namespace LL;

public static class JsonValidator
{
    public static void Handle(string[] args)
    {
        if (args.Length == 0 || args[0] == "help")
        {
            UI.PrintInfo("用法:");
            UI.PrintInfo("  json-validate <json_string> [--file <file_path>]");
            UI.PrintInfo("  json-validate --file <file_path>");
            UI.PrintInfo("校验 JSON 格式，不合格输出具体错误。长 JSON 会省略中间部分。");
            return;
        }

        string json = null;
        string filePath = null;

        if (args[0] == "--file" && args.Length >= 2)
        {
            filePath = args[1];
        }
        else if (args.Length >= 1)
        {
            json = string.Join(" ", args);
        }

        if (filePath != null)
        {
            if (!File.Exists(filePath))
            {
                UI.PrintError("文件不存在。");
                return;
            }
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                UI.PrintError($"读取文件失败: {ex.Message}");
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            UI.PrintError("请提供 JSON 字符串或文件路径。");
            return;
        }

        ValidateJson(json);
    }

    private static void ValidateJson(string json)
    {
        try
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                UI.PrintSuccess("JSON 格式正确。");
            }
        }
        catch (JsonException ex)
        {
            UI.PrintError($"JSON 格式错误: {ex.Message}");
            // Optionally show position
            if (ex.LineNumber > 0)
            {
                UI.PrintInfo($"错误位置: 第 {ex.LineNumber} 行，第 {ex.BytePositionInLine} 字符。");
            }
        }
        catch (Exception ex)
        {
            UI.PrintError($"校验失败: {ex.Message}");
        }

        // Display truncated JSON if too long
        if (json.Length > 200)
        {
            string truncated = json.Substring(0, 100) + " ... " + json.Substring(json.Length - 100);
            UI.PrintInfo($"JSON 内容 (省略): {truncated}");
        }
        else
        {
            UI.PrintInfo($"JSON 内容: {json}");
        }
    }
}