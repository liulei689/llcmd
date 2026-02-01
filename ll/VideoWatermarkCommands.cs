using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LL;

public static class VideoWatermarkCommands
{
    private static string GetFfmpegPath()
        => Path.Combine(AppContext.BaseDirectory, "tools", "bin", "ffmpeg.exe");

    private static string GetFfprobePath()
        => Path.Combine(AppContext.BaseDirectory, "tools", "bin", "ffprobe.exe");

    private static double? GetDurationSeconds(string filePath)
    {
        var ffprobePath = GetFfprobePath();
        if (!File.Exists(ffprobePath)) return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v quiet -print_format compact=nk=1:p=0 -show_entries format=duration \"{filePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            var outp = p!.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            if (p.ExitCode != 0) return null;

            if (double.TryParse(outp, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d;
        }
        catch { }

        return null;
    }

    private static bool RunFfmpegWithProgress(string label, string ffmpegPath, string arguments, double? totalSeconds)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-hide_banner -nostats -y -progress pipe:2 {arguments}",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi);
        if (p == null) return false;

        var sw = Stopwatch.StartNew();
        string? line;
        long outTimeMs = 0;
        string speed = "";

        while ((line = p.StandardError.ReadLine()) != null)
        {
            if (line.StartsWith("out_time_ms=", StringComparison.Ordinal))
            {
                long.TryParse(line[11..], out outTimeMs);
            }
            else if (line.StartsWith("speed=", StringComparison.Ordinal))
            {
                speed = line[6..].Trim();
            }
            else if (line.StartsWith("progress=", StringComparison.Ordinal))
            {
                var currentSec = outTimeMs / 1000000.0;
                string etaText = "--";
                string pctText = "--";

                if (totalSeconds is > 0)
                {
                    var pct = Math.Clamp(currentSec / totalSeconds.Value, 0, 1);
                    pctText = (pct * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
                    if (pct > 0.001)
                    {
                        var eta = TimeSpan.FromSeconds(sw.Elapsed.TotalSeconds * (1 / pct - 1));
                        etaText = eta.ToString(@"hh\:mm\:ss");
                    }
                }

                var tText = TimeSpan.FromSeconds(currentSec).ToString(@"hh\:mm\:ss");
                Console.Write($"\r[{label}] {pctText} t={tText} ETA={etaText} speed={speed}   ");

                if (line.EndsWith("end", StringComparison.Ordinal))
                    break;
            }
        }

        p.WaitForExit();
        Console.WriteLine();
        return p.ExitCode == 0;
    }

    private static string EscapeDrawText(string text)
    {
        // ffmpeg drawtext 需要转义 : \ ' 
        return text
            .Replace("\\", "\\\\")
            .Replace(":", "\\:")
            .Replace("'", "\\'")
            .Replace("\n", "\\n");
    }

    private static string? TryFindFontFile()
    {
        // 优先选择常见中文字体，避免中文显示为方框。
        // Windows:
        //  - 微软雅黑: msyh.ttc
        //  - 宋体: simsun.ttc
        //  - 等线: deng.ttf
        //  - 思源黑体(若安装): SourceHanSansCN-Regular.otf/ttf
        var winFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!string.IsNullOrWhiteSpace(winFonts) && Directory.Exists(winFonts))
        {
            string[] candidates =
            [
                Path.Combine(winFonts, "msyh.ttc"),
                Path.Combine(winFonts, "msyhbd.ttc"),
                Path.Combine(winFonts, "simsun.ttc"),
                Path.Combine(winFonts, "simhei.ttf"),
                Path.Combine(winFonts, "deng.ttf"),
                Path.Combine(winFonts, "dengb.ttf"),
                Path.Combine(winFonts, "SourceHanSansCN-Regular.otf"),
                Path.Combine(winFonts, "SourceHanSansCN-Regular.ttf"),
            ];

            foreach (var f in candidates)
            {
                if (File.Exists(f)) return f;
            }

            // 兜底：找第一个看起来像中文字体的
            try
            {
                var any = Directory.EnumerateFiles(winFonts, "*.tt*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(p => p.Contains("msyh", StringComparison.OrdinalIgnoreCase)
                                         || p.Contains("simsun", StringComparison.OrdinalIgnoreCase)
                                         || p.Contains("simhei", StringComparison.OrdinalIgnoreCase)
                                         || p.Contains("sourcehan", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(any)) return any;
            }
            catch { }
        }

        return null;
    }

    public static void Watermark(string[] args)
    {
        if (args.Length < 2)
        {
            UI.PrintError("用法: video watermark <text> <input.mp4> [output.mp4]");
            UI.PrintInfo("或:   video watermark <text> <folder> [outDir]  (批量输出 *_wm.mp4)");
            return;
        }

        var text = args[0];
        var inputPath = args[1];
        var outputPath = args.Length >= 3 ? args[2] : "";

        var ffmpegPath = GetFfmpegPath();
        if (!File.Exists(ffmpegPath))
        {
            UI.PrintError($"未找到 ffmpeg.exe: {ffmpegPath}");
            return;
        }

        if (Directory.Exists(inputPath))
        {
            // 默认：在源目录下新建 watermarked 文件夹统一输出
            var outDir = args.Length >= 3 ? args[2] : Path.Combine(inputPath, "watermarked");
            Directory.CreateDirectory(outDir);

            var files = Directory.GetFiles(inputPath, "*.mp4", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
            {
                UI.PrintError("文件夹内未找到 mp4");
                return;
            }

            UI.PrintInfo($"批量加水印: {files.Length} 个文件");
            for (int i = 0; i < files.Length; i++)
            {
                var f = files[i];
                var outFile = Path.Combine(outDir, Path.GetFileNameWithoutExtension(f) + "_wm.mp4");
                if (File.Exists(outFile))
                {
                    UI.PrintInfo($"[{i + 1}/{files.Length}] 已存在，跳过: {Path.GetFileName(outFile)}");
                    continue;
                }

                UI.PrintInfo($"[{i + 1}/{files.Length}] 处理中: {Path.GetFileName(f)}");
                if (!WatermarkSingle(text, f, outFile, ffmpegPath))
                {
                    UI.PrintError($"失败(已跳过): {f}");
                }
            }
            return;
        }

        if (!File.Exists(inputPath))
        {
            UI.PrintError($"输入不存在: {inputPath}");
            return;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            var dir = Path.GetDirectoryName(inputPath) ?? ".";
            outputPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(inputPath) + "_wm.mp4");
        }

        if (File.Exists(outputPath))
        {
            UI.PrintError($"输出已存在: {outputPath}");
            return;
        }

        if (WatermarkSingle(text, inputPath, outputPath, ffmpegPath))
            UI.PrintSuccess($"完成: {outputPath}");
        else
            UI.PrintError("加水印失败");
    }

    private static bool WatermarkSingle(string text, string inputPath, string outputPath, string ffmpegPath)
    {
        var dur = GetDurationSeconds(inputPath);

        var fontFile = TryFindFontFile();
        var fontPart = fontFile != null ? $"fontfile='{EscapeDrawText(fontFile)}':" : "";

        // 大气但不挡内容：右上角、粉红系、带高亮描边和阴影（提高可读性但尽量不抢画面）
        // 注意：fontfile 依赖系统字体搜索；如果你想指定字体文件路径，可再加参数。
        var safe = EscapeDrawText(text);
        var draw = "drawtext=" +
                   fontPart +
                   $"text='{safe}':" +
                   // 诱惑粉：主色偏粉，更亮更实
                   "fontcolor=#ff2d95@0.92:" +
                   "fontsize=h*0.050:" +
                   // 高级感：暖色高光描边（避免灰边）+ 更干净的阴影，增强立体感
                   "borderw=3:bordercolor=#ffd1ea@0.80:" +
                   "shadowx=3:shadowy=3:shadowcolor=black@0.45:" +
                   // 右上角，留边距
                   "x=w-tw-40:y=30";

        var args = $"-i \"{inputPath}\" -vf \"{draw}\" -c:v libx264 -preset fast -crf 18 -pix_fmt yuv420p -c:a copy \"{outputPath}\"";
        return RunFfmpegWithProgress("水印", ffmpegPath, args, dur);
    }
}
