using System;
using System.IO;
using System.Linq;

namespace LL;

public static class VideoCommands
{
    public static void Run(string[] args)
    {
        if (args.Length == 0)
        {
            UI.PrintError("用法: video <merge|watermark> ...");
            UI.PrintInfo("  video merge <folder> | video merge <output> <input1> <input2> ...");
            UI.PrintInfo("  video watermark <text> <input.mp4> [output.mp4]");
            UI.PrintInfo("  video watermark <text> <folder>   (批量输出到 *_wm.mp4)");
            return;
        }

        var sub = args[0].Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        switch (sub)
        {
            case "merge":
                VideoMergeCommands.Merge(rest);
                return;
            case "watermark":
            case "wm":
                VideoWatermarkCommands.Watermark(rest);
                return;
            default:
                UI.PrintError($"未知子命令: {args[0]}");
                return;
        }
    }
}
