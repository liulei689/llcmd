namespace LL;

public static class HistoryCommands
{
    public static void Show(string[] args)
    {
        int n = 30;
        if (args.Length > 0 && int.TryParse(args[0], out var v))
            n = v;

        var lines = HistoryManager.ReadLast(n);
        if (lines.Count == 0)
        {
            UI.PrintInfo("暂无历史记录");
            return;
        }

        UI.PrintHeader($"历史记录 (最近 {lines.Count} 条)");
        int startIndex = Math.Max(0, lines.Count - n);
        for (int i = 0; i < lines.Count; i++)
        {
            UI.PrintItem($"{startIndex + i + 1,3}", lines[i]);
        }
    }
}
