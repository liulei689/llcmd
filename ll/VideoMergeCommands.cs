using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LL;

public static class VideoMergeCommands
{
    private enum Orientation
    {
        Landscape,
        Portrait
    }

    private static Orientation GetOrientation((int width, int height) s)
        => s.width >= s.height ? Orientation.Landscape : Orientation.Portrait;

    private static string GetFfmpegPath()
        => Path.Combine(AppContext.BaseDirectory, "tools", "bin", "ffmpeg.exe");

    private static string GetFfprobePath()
        => Path.Combine(AppContext.BaseDirectory, "tools", "bin", "ffprobe.exe");

    private static (int width, int height) GetVideoSize(string filePath)
    {
        var ffprobePath = GetFfprobePath();
        if (!File.Exists(ffprobePath)) return (0, 0);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v quiet -print_format json -show_streams -show_entries stream=width,height,tags:stream_tags=rotate \"{filePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            var outp = p!.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) return (0, 0);

            using var doc = JsonDocument.Parse(outp);
            if (!doc.RootElement.TryGetProperty("streams", out var streams)) return (0, 0);

            foreach (var s in streams.EnumerateArray())
            {
                if (!s.TryGetProperty("width", out var w) || !w.TryGetInt32(out var wi)) continue;
                if (!s.TryGetProperty("height", out var h) || !h.TryGetInt32(out var hi)) continue;

                int rot;
                if (s.TryGetProperty("tags", out var tags)
                    && tags.TryGetProperty("rotate", out var r)
                    && r.TryGetInt32(out rot)
                    && (rot == 90 || rot == 270))
                {
                    return (hi, wi);
                }

                return (wi, hi);
            }
        }
        catch
        {
        }

        return (0, 0);
    }

    private static double? GetDurationSeconds(string filePath)
    {
        var ffprobePath = GetFfprobePath();
        if (!File.Exists(ffprobePath)) return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v quiet -print_format json -show_format \"{filePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            var outp = p!.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) return null;

            using var doc = JsonDocument.Parse(outp);
            if (!doc.RootElement.TryGetProperty("format", out var format)) return null;
            if (!format.TryGetProperty("duration", out var dur)) return null;
            var s = dur.GetString();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        }
        catch
        {
        }

        return null;
    }

    private static bool RunFfmpegWithProgress(string stage, int index, int total, string ffmpegPath, string arguments, double? totalSeconds)
    {
        // ffmpeg -progress pipe:2 输出 key=value，包含 out_time_ms / speed / progress
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
                Console.Write($"\r[{stage} {index}/{total}] {pctText} t={tText} ETA={etaText} speed={speed}   ");

                if (line.EndsWith("end", StringComparison.Ordinal))
                    break;
            }
        }

        p.WaitForExit();
        Console.WriteLine();
        return p.ExitCode == 0;
    }

    private static bool MergeGroup(
        string stageLabel,
        IReadOnlyList<string> inputs,
        string output,
        (int width, int height) target,
        string ffmpegPath,
        string? watermarkText)
    {
        if (inputs.Count < 2)
        {
            UI.PrintInfo($"{stageLabel}: 可用视频不足 2 个，跳过输出。");
            return false;
        }

        if (File.Exists(output))
        {
            UI.PrintError($"{stageLabel}: 输出文件已存在: {output}");
            return false;
        }

        var concatFile = Path.GetTempFileName();
        var tempFiles = new List<string>();

        try
        {
            UI.PrintInfo($"{stageLabel}: 输出 {target.width}x{target.height}，保持比例、不裁剪；单个失败会跳过。");

            for (int i = 0; i < inputs.Count; i++)
            {
                var inFile = inputs[i];
                var tmp = Path.Combine(Path.GetTempPath(), $"ll_mergev_{Guid.NewGuid():N}.mp4");
                var dur = GetDurationSeconds(inFile);

                // 保持比例，不裁剪。
                // 说明：在不允许黑边(pad) 且不允许裁剪(crop) 且不允许拉伸的前提下，
                // 只能按比例缩放到不超过目标尺寸，因此部分视频不会“铺满”(
                // 这是宽高比不同导致的必然结果)。
                var vf = $"scale=w={target.width}:h={target.height}:force_original_aspect_ratio=decrease";
                var argsEnc = $"-i \"{inFile}\" -vf \"{vf}\" -c:v libx264 -preset fast -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k \"{tmp}\"";

                if (!RunFfmpegWithProgress(stageLabel, i + 1, inputs.Count, ffmpegPath, argsEnc, dur))
                {
                    UI.PrintError($"{stageLabel}: 转码失败，已跳过: {inFile}");
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    continue;
                }

                tempFiles.Add(tmp);
            }

            if (tempFiles.Count < 2)
            {
                UI.PrintError($"{stageLabel}: 可用视频不足 2 个（其余已跳过），无法合并。");
                return false;
            }

            using (var w = new StreamWriter(concatFile))
            {
                foreach (var f in tempFiles)
                    w.WriteLine($"file '{Path.GetFullPath(f)}'");
            }

            var totalDur = tempFiles.Select(GetDurationSeconds).Where(d => d is > 0).Sum(d => d!.Value);
            var argsMerge = $"-f concat -safe 0 -i \"{concatFile}\" -c:v libx264 -preset fast -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k \"{output}\"";

            if (!RunFfmpegWithProgress($"{stageLabel}-合并", 1, 1, ffmpegPath, argsMerge, totalDur > 0 ? totalDur : null))
            {
                UI.PrintError($"{stageLabel}: 合并失败，已跳过输出: {output}");
                return false;
            }

            UI.PrintSuccess($"{stageLabel}: 合并完成: {output}");

            if (!string.IsNullOrWhiteSpace(watermarkText))
            {
                var outDir = Path.Combine(Path.GetDirectoryName(output) ?? ".", "watermarked");
                Directory.CreateDirectory(outDir);
                var outWm = Path.Combine(outDir, Path.GetFileNameWithoutExtension(output) + "_wm.mp4");
                UI.PrintInfo($"{stageLabel}: 追加水印 -> {Path.GetFileName(outWm)}");
                // 失败不影响合并结果
                try
                {
                    VideoWatermarkCommands.Watermark(new[] { watermarkText!, output, outWm });
                }
                catch
                {
                    UI.PrintError($"{stageLabel}: 水印失败(已跳过)");
                }
            }
            return true;
        }
        finally
        {
            try { if (File.Exists(concatFile)) File.Delete(concatFile); } catch { }
            foreach (var f in tempFiles)
            {
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
        }
    }

    public static void Merge(string[] args)
    {
        string? watermarkText = null;
        var a = new List<string>(args);
        for (int i = 0; i < a.Count; i++)
        {
            if (string.Equals(a[i], "--wm", StringComparison.OrdinalIgnoreCase) && i + 1 < a.Count)
            {
                watermarkText = a[i + 1];
                a.RemoveAt(i + 1);
                a.RemoveAt(i);
                break;
            }
        }

        args = a.ToArray();

        if (args.Length < 1)
        {
            UI.PrintError("用法: video merge <folder> 或 video merge <output> <input1> <input2> ...");
            UI.PrintInfo("可选: --wm <text>  合并后追加水印输出到 watermarked 子目录");
            UI.PrintInfo("说明: mergev 会按横屏/竖屏分别输出两个文件，保持比例、不裁剪；单个失败会跳过。");
            return;
        }

        string[] inputs;
        string outputBase;

        if (args.Length == 1)
        {
            var folder = args[0];
            if (!Directory.Exists(folder))
            {
                UI.PrintError($"文件夹不存在: {folder}");
                return;
            }

            inputs = Directory.GetFiles(folder, "*.mp4", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (inputs.Length < 2)
            {
                UI.PrintError($"文件夹中至少需要 2 个 MP4 文件，当前: {inputs.Length}");
                return;
            }

            outputBase = Path.Combine(folder, "merged");
        }
        else
        {
            outputBase = Path.ChangeExtension(args[0], null) ?? args[0];
            inputs = args.Skip(1).ToArray();
        }

        foreach (var input in inputs)
        {
            if (!File.Exists(input))
            {
                UI.PrintError($"输入文件不存在: {input}");
                return;
            }
        }

        var ffmpegPath = GetFfmpegPath();
        if (!File.Exists(ffmpegPath))
        {
            UI.PrintError($"未找到 ffmpeg.exe: {ffmpegPath}");
            return;
        }

        // 按方向分组：横屏就输出横屏文件，竖屏就输出竖屏文件
        var landscape = new List<string>();
        var portrait = new List<string>();

        int landW = 0, landH = 0;
        int portW = 0, portH = 0;

        foreach (var f in inputs)
        {
            var s = GetVideoSize(f);
            if (s.width <= 0 || s.height <= 0)
            {
                UI.PrintError($"无法获取分辨率，已跳过: {f}");
                continue;
            }

            if (GetOrientation(s) == Orientation.Landscape)
            {
                landscape.Add(f);
                if (s.width > landW) landW = s.width;
                if (s.height > landH) landH = s.height;
            }
            else
            {
                portrait.Add(f);
                if (s.width > portW) portW = s.width;
                if (s.height > portH) portH = s.height;
            }
        }

        if ((landW & 1) == 1) landW++;
        if ((landH & 1) == 1) landH++;
        if ((portW & 1) == 1) portW++;
        if ((portH & 1) == 1) portH++;

        var outLand = outputBase + "_landscape.mp4";
        var outPort = outputBase + "_portrait.mp4";

        bool any = false;
        if (landscape.Count >= 2)
            any |= MergeGroup("横屏", landscape, outLand, (landW, landH), ffmpegPath, watermarkText);
        else
            UI.PrintInfo("横屏: 数量不足 2，跳过。");

        if (portrait.Count >= 2)
            any |= MergeGroup("竖屏", portrait, outPort, (portW, portH), ffmpegPath, watermarkText);
        else
            UI.PrintInfo("竖屏: 数量不足 2，跳过。");

        if (!any)
            UI.PrintError("没有输出任何合并文件（可能都被跳过或数量不足）。");
    }
}
