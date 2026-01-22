using System;
using System.Collections.Generic;
using System.Linq;

namespace LL;

/// <summary>
/// 指令建议管理器，用于识别用户输入的错误指令并提供相似指令建议
/// </summary>
public static class SuggestionManager
{
    /// <summary>
    /// 获取指令建议列表
    /// </summary>
    /// <param name="input">用户输入的指令</param>
    /// <param name="commands">现有指令列表 (Id, Name, Description)</param>
    /// <param name="maxSuggestions">最大建议数量</param>
    /// <returns>相似指令列表</returns>
    public static List<(int Id, string Name, string Description)> GetSuggestions(string input, List<(int Id, string Name, string Description)> commands, int maxSuggestions = 5)
    {
        if (string.IsNullOrWhiteSpace(input) || commands == null || commands.Count == 0)
            return new List<(int, string, string)>();

        bool isChinese = IsChinese(input);

        // 计算每个指令的相似度
        var suggestions = commands
            .Select(cmd => {
                string target = isChinese ? cmd.Description : cmd.Name;
                int dist = LevenshteinDistance(input.ToLower(), target.ToLower());
                // 如果输入是目标的子串或目标是输入的子串，设距离为0，确保匹配
                if (target.ToLower().Contains(input.ToLower()) || input.ToLower().Contains(target.ToLower()))
                {
                    dist = 0;
                }
                return (Command: cmd, Distance: dist);
            })
            .Where(x => x.Distance <= Math.Min(2, input.Length / 3)) // 更严格的距离阈值
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Command.Name.Length)
            .Take(maxSuggestions)
            .Select(x => x.Command)
            .ToList();

        return suggestions;
    }

    /// <summary>
    /// 计算两个字符串的Levenshtein距离（编辑距离）
    /// </summary>
    private static int LevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        for (int i = 0; i <= n; d[i, 0] = i++) ;
        for (int j = 0; j <= m; d[0, j] = j++) ;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

    /// <summary>
    /// 判断字符串是否包含中文字符
    /// </summary>
    private static bool IsChinese(string input)
    {
        return input.Any(c => c >= 0x4E00 && c <= 0x9FFF);
    }

    /// <summary>
    /// 显示指令建议
    /// </summary>
    /// <param name="input">用户输入</param>
    /// <param name="suggestions">建议列表</param>
    public static void ShowSuggestions(string input, List<(int Id, string Name, string Description)> suggestions)
    {
        if (suggestions.Count == 0)
        {
            UI.PrintError($"指令 '{input}' 不存在，请检查拼写或使用 'ls' 查看所有指令。");
            return;
        }

        UI.PrintError($"指令 '{input}' 不存在。您可能想要输入以下指令之一：");
        foreach (var suggestion in suggestions)
        {
            UI.PrintInfo($"  - {suggestion.Id} {suggestion.Name} {suggestion.Description}");
        }
        UI.PrintInfo("请重新输入正确的指令，或使用 'ls' 查看所有可用指令。");
    }
}