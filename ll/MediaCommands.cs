using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using LL;

public static class MediaCommands
{
    private static (int width, int height) GetVideoSize(string filePath, string ffprobePath)
    {
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

            using (var p = Process.Start(psi))
            {
                string outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0) return (0, 0);
                try
                {
                    using var doc = JsonDocument.Parse(outp);
                    if (doc.RootElement.TryGetProperty("streams", out var streams) && streams.GetArrayLength() > 0)
                    {
                        foreach (var s in streams.EnumerateArray())
                        {
                            int wi = 0, hi = 0, rot = 0;
                            if (s.TryGetProperty("width", out var w) && w.TryGetInt32(out wi) &&
                                s.TryGetProperty("height", out var h) && h.TryGetInt32(out hi))
                            {
                                // 检查 tags/rotate 或 rotate 字段
                                if ((s.TryGetProperty("tags", out var tags) && tags.TryGetProperty("rotate", out var rotate1) && rotate1.TryGetInt32(out rot) && (rot == 90 || rot == 270))
                                    || (s.TryGetProperty("rotate", out var rotate2) && rotate2.TryGetInt32(out rot) && (rot == 90 || rot == 270)))
                                {
                                    // 旋转视频，宽高互换
                                    return (hi, wi);
                                }
                                return (wi, hi);
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
        return (0, 0);
    }

    private static (int width, int height) GetVideoSizeFallback(string filePath, string ffmpegPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-i \"{filePath}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            // look for pattern WxH like 1920x1080
            var m = System.Text.RegularExpressions.Regex.Match(err, @"(\d{2,5})x(\d{2,5})");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int w) && int.TryParse(m.Groups[2].Value, out int h))
                return (w, h);
        }
        catch { }
        return (0, 0);
    }

    private static bool HasAudioStream(string filePath, string ffprobePath, string ffmpegPath)
    {
        if (File.Exists(ffprobePath))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v quiet -print_format json -show_streams \"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                string outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                using var doc = JsonDocument.Parse(outp);
                if (doc.RootElement.TryGetProperty("streams", out var streams))
                {
                    foreach (var s in streams.EnumerateArray())
                    {
                        if (s.TryGetProperty("codec_type", out var t) && t.GetString() == "audio") return true;
                    }
                }
            }
            catch { }
            return false;
        }
        // fallback parse ffmpeg stderr
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-i \"{filePath}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return err.Contains("Audio:");
        }
        catch { }
        return false;
    }

    public static void Merge(string[] args)
    {
        if (args.Length < 1)
        {
            UI.PrintError("用法: merge <folder> 或 merge <output> <input1> <input2> ...");
            UI.PrintInfo("文件夹模式: 自动合并文件夹中的所有 MP4 文件为 merged.mp4。");
            UI.PrintInfo("手动模式: 指定输出文件和输入文件。");
            return;
        }

        string[] inputs;
        string output;

        if (args.Length == 1)
        {
            string folder = args[0];
            if (!Directory.Exists(folder))
            {
                UI.PrintError($"文件夹不存在: {folder}");
                return;
            }
            inputs = Directory.GetFiles(folder, "*.mp4", SearchOption.TopDirectoryOnly);
            if (inputs.Length < 2)
            {
                UI.PrintError($"文件夹中至少需要 2 个 MP4 文件，当前: {inputs.Length}");
                return;
            }
            output = Path.Combine(folder, "merged.mp4");
        }
        else
        {
            output = args[0];
            inputs = args.Skip(1).ToArray();
        }

        foreach (string input in inputs)
        {
            if (!File.Exists(input))
            {
                UI.PrintError($"输入文件不存在: {input}");
                return;
            }
        }

        if (File.Exists(output))
        {
            UI.PrintError($"输出文件已存在: {output}");
            return;
        }

        var ffmpegPath = Path.Combine(AppContext.BaseDirectory, "tools", "bin", "ffmpeg.exe");
        if (!File.Exists(ffmpegPath))
        {
            UI.PrintError($"未找到 ffmpeg.exe: {ffmpegPath}");
            return;
        }

        string concatFile = Path.GetTempFileName();
        try
        {
            UI.PrintInfo($"开始合并 {inputs.Length} 个视频文件...");

            // 直接 concat copy，快速且不改画质（如果编码/分辨率不同，会失败）
            using (StreamWriter writer = new StreamWriter(concatFile))
            {
                foreach (string input in inputs)
                {
                    writer.WriteLine($"file '{Path.GetFullPath(input)}'");
                }
            }
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-f concat -safe 0 -i \"{concatFile}\" -c copy \"{output}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (Process process = Process.Start(psi))
            {
                process.ErrorDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data) && e.Data.Contains("time="))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(e.Data, @"time=([0-9:.]+)");
                        if (match.Success)
                        {
                            Console.Write($"\r[合并] 进度: {match.Groups[1].Value}   ");
                        }
                    }
                };
                process.BeginErrorReadLine();
                process.WaitForExit();
                Console.WriteLine();
                if (process.ExitCode == 0)
                {
                    UI.PrintSuccess($"合并完成: {output}");
                }
                else
                {
                    UI.PrintError($"合并失败，退出码: {process.ExitCode}。可能原因：视频编码/分辨率不同，请确保所有视频相同格式。");
                }
            }
        }
        catch (Exception ex)
        {
            UI.PrintError($"合并出错: {ex.Message}");
        }
        finally
        {
            if (File.Exists(concatFile)) File.Delete(concatFile);
        }
    }
}