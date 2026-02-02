using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LL;

public static class VideoInspectCommands
{
    private static string ToolsBin => Path.Combine(AppContext.BaseDirectory, "tools", "bin");
    private static string FfprobePath => Path.Combine(ToolsBin, "ffprobe.exe");

    public static void Inspect(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            PrintHelp();
            return;
        }

        bool raw = args.Any(a => a is "--raw" or "-r");
        var path = args[0].Trim('"');
        if (!File.Exists(path))
        {
            UI.PrintError($"文件不存在: {path}");
            return;
        }

        UI.PrintHeader("视频特征分析");
        UI.PrintResult("路径", Path.GetFullPath(path));

        var fi = new FileInfo(path);
        UI.PrintResult("大小", Utils.FormatSize(fi.Length));
        UI.PrintResult("修改时间", fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        Console.WriteLine();

        PrintHeaderBytes(path);
        Console.WriteLine();

        PrintContainerGuesses(path);
        Console.WriteLine();

        PrintEntropy(path);
        Console.WriteLine();

        PrintMidstreamSignatures(path);
        Console.WriteLine();

        PrintSecurityHeuristics(path);
        Console.WriteLine();

        RunFfprobeIfAvailable(path, raw);
    }

    private static void PrintHelp()
    {
        UI.PrintInfo("用法: video inspect <file>\n" +
                     "可选: --raw  输出 ffprobe 原始 JSON\n" +
                     "说明: 尽可能分析文件是否包含视频特征/容器结构。\n" +
                     "  - 会尝试识别常见容器签名 (mp4/mkv/avi/ts/flv/webm/ogg/...)\n" +
                     "  - 输出头部十六进制/ASCII\n" +
                     "  - 简单判断是否高熵(疑似加密/压缩)\n" +
                     "  - 若存在 ffprobe.exe，会调用 ffprobe 深度探测(最可靠)\n" +
                     "提示: 对 .llv 这类自定义加密格式，通常 ffprobe 无法直接识别；本命令可帮助判断“前面是否塞了自定义头/是否像加密数据”。");
    }

    private static void PrintMidstreamSignatures(string path)
    {
        UI.PrintHeader("内容相似度推断(外行可读)");

        // 只取少量数据做启发式：开头/中间/末尾各一段
        var fi = new FileInfo(path);
        long len = fi.Length;
        if (len <= 0)
        {
            UI.PrintError("文件为空");
            return;
        }

        const int window = 256 * 1024;
        var parts = new (string Name, long Offset)[]
        {
            ("开头", 0),
            ("中间", Math.Max(0, len / 2 - window / 2)),
            ("末尾", Math.Max(0, len - window))
        };

        int hitTs = 0;
        int hitMp4Boxes = 0;
        int hitNal = 0;

        int totalMp4BoxHits = 0;
        int totalNalStarts = 0;

        foreach (var part in parts)
        {
            var buf = ReadWindow(path, part.Offset, window);
            if (buf.Length == 0) continue;

            var tsScore = TsSyncScore(buf);
            var mp4BoxHits = Mp4BoxHitCount(buf);
            var nalStarts = NalStartCodeCount(buf);

            totalMp4BoxHits += mp4BoxHits;
            totalNalStarts += nalStarts;

            // 把“专业指标”转换为是否命中
            if (tsScore >= 3) hitTs++;
            if (mp4BoxHits >= 2) hitMp4Boxes++;
            if (nalStarts >= 32) hitNal++;
        }

        // 相似度/置信度打分（0-100）
        int tsScoreFinal = hitTs * 33;          // 0/33/66/99
        int mp4ScoreFinal = hitMp4Boxes * 33;
        int nalScoreFinal = hitNal * 33;

        // 取最高作为“最像哪类”
        int best = Math.Max(tsScoreFinal, Math.Max(mp4ScoreFinal, nalScoreFinal));
        string bestType = best == tsScoreFinal ? "MPEG-TS 分片类" : best == mp4ScoreFinal ? "MP4/ISO 容器类" : "裸 H26x 码流类";

        UI.PrintResult("相似度(MPEG-TS)", $"{tsScoreFinal}/100");
        UI.PrintResult("相似度(MP4/ISO Box)", $"{mp4ScoreFinal}/100");
        UI.PrintResult("相似度(H26x 起始码)", $"{nalScoreFinal}/100");
        UI.PrintResult("初步结论", best <= 0 ? "未发现明显的“未加密视频结构”特征(可能是加密/或容器结构不明显)" : $"更像: {bestType}");

        UI.PrintResult("细节(统计)", $"MP4 box 命中总数={totalMp4BoxHits}, NAL 起始码总数={totalNalStarts}");
        UI.PrintInfo("说明: 你输入的是“正常 MP4/MKV 文件”也可能出现“中段特征不明显”，因为很多容器结构集中在文件头/索引区，而中段主要是压缩媒体数据。最可靠判断仍然是 ffprobe。\n" +
                     "如果该文件被真正加密(例如 .llv)，这些结构特征会被破坏，扫描命中会更低。");
    }

    private static void PrintSecurityHeuristics(string path)
    {
        UI.PrintHeader("可分析性/加密判断(启发式)");

        // 头部是否为 LLV
        var head4 = new byte[4];
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.ReadExactly(head4);
        }
        catch
        {
            return;
        }

        var magic = Encoding.ASCII.GetString(head4);
        bool isLlv = magic is "LLV1" or "LLV2";

        double entropy = 0;
        try
        {
            var sample = ReadWindow(path, 0, 256 * 1024);
            entropy = ShannonEntropy(sample);
        }
        catch { }

        UI.PrintResult("自定义格式标识", isLlv ? "检测到 LLV1/LLV2" : "未检测到 LLV magic");
        UI.PrintResult("样本熵", entropy.ToString("0.000", CultureInfo.InvariantCulture));

        if (isLlv || entropy >= 7.5)
        {
            UI.PrintResult("结论", "很可能是加密/高度随机数据：能被识别为“加密文件”，但不能从中推断出原视频编码细节");
        }
        else
        {
            UI.PrintResult("结论", "更像标准媒体/压缩数据：通常可通过容器/码流规律识别出类型(甚至从中段恢复解析)\n例如 MPEG-TS 的 0x47 周期、MP4 box 结构、NAL 起始码等。");
        }

        UI.PrintInfo("提示: 如果只是“在视频前面加了一段自定义头”但未加密，很多工具仍可从中段找到容器结构并恢复播放；真正加密(如 AES-GCM)会破坏这些规律。");
    }

    private static byte[] ReadWindow(string path, long offset, int size)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (offset > 0) fs.Position = Math.Min(offset, fs.Length);
            int toRead = (int)Math.Min(size, Math.Max(0, fs.Length - fs.Position));
            if (toRead <= 0) return Array.Empty<byte>();
            var buf = new byte[toRead];
            fs.ReadExactly(buf);
            return buf;
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private static int TsSyncScore(ReadOnlySpan<byte> data)
    {
        // 简单计数：在 0..(3*188) 的几个位置是否出现 0x47
        // 3 表示较强命中。
        int score = 0;
        if (data.Length >= 188 * 2)
        {
            if (data[0] == 0x47) score++;
            if (data.Length > 188 && data[188] == 0x47) score++;
            if (data.Length > 376 && data[376] == 0x47) score++;
        }
        return score;
    }

    private static int NalStartCodeCount(ReadOnlySpan<byte> data)
    {
        // 统计 00 00 01 或 00 00 00 01
        // 备注：这里只做“起始码数量”统计，不解析 NAL 类型，避免复杂度。
        int count = 0;
        for (int i = 0; i + 4 < data.Length; i++)
        {
            if (data[i] == 0x00 && data[i + 1] == 0x00)
            {
                if (data[i + 2] == 0x01)
                {
                    count++;
                    i += 3;
                }
                else if (data[i + 2] == 0x00 && data[i + 3] == 0x01)
                {
                    count++;
                    i += 4;
                }
            }
        }
        return count;
    }

    private static int Mp4BoxHitCount(ReadOnlySpan<byte> data)
    {
        // MP4/ISO BMFF 常见 box：ftyp/moov/mdat/free/skip/mvhd/trak/mdia/minf/stbl
        // 这里做一个非常粗略的“4 字符类型码”扫描，避免复杂解析。
        ReadOnlySpan<byte> ftyp = "ftyp"u8;
        ReadOnlySpan<byte> moov = "moov"u8;
        ReadOnlySpan<byte> mdat = "mdat"u8;
        ReadOnlySpan<byte> free = "free"u8;
        ReadOnlySpan<byte> skip = "skip"u8;

        int hits = 0;
        for (int i = 4; i + 4 <= data.Length; i++)
        {
            var t = data.Slice(i, 4);
            if (t.SequenceEqual(ftyp) || t.SequenceEqual(moov) || t.SequenceEqual(mdat) || t.SequenceEqual(free) || t.SequenceEqual(skip))
                hits++;
        }
        return hits;
    }

    private static void PrintHeaderBytes(string path)
    {
        UI.PrintHeader("文件头(前 256 字节)");
        var buf = new byte[256];
        int read;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            read = fs.Read(buf, 0, buf.Length);
        }

        if (read <= 0)
        {
            UI.PrintError("无法读取文件头");
            return;
        }

        var hex = BitConverter.ToString(buf, 0, read).Replace("-", " ");
        Console.WriteLine(hex);

        Console.WriteLine();
        UI.PrintResult("ASCII(可打印字符)", ToPrintableAscii(buf.AsSpan(0, read)));

        // 常见 magic 输出
        var magic4 = read >= 4 ? Encoding.ASCII.GetString(buf, 0, 4) : "";
        UI.PrintResult("Magic(前4字节)", EscapeNonPrintable(magic4));

        // MP4: bytes 4..7 = 'ftyp'
        if (read >= 12 && buf[4] == (byte)'f' && buf[5] == (byte)'t' && buf[6] == (byte)'y' && buf[7] == (byte)'p')
        {
            var brand = Encoding.ASCII.GetString(buf, 8, 4);
            UI.PrintResult("MP4/ISO BMFF", $"疑似，ftyp brand={EscapeNonPrintable(brand)}");
        }
    }

    private static void PrintContainerGuesses(string path)
    {
        UI.PrintHeader("容器签名推断");

        byte[] head = new byte[64];
        int read;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            read = fs.Read(head, 0, head.Length);
        }

        ReadOnlySpan<byte> h = head.AsSpan(0, read);

        void Guess(string label, bool ok, string reason)
            => UI.PrintResult(label, ok ? $"可能 ({reason})" : "否");

        // MP4/ISO BMFF
        bool mp4 = read >= 12 && h[4] == (byte)'f' && h[5] == (byte)'t' && h[6] == (byte)'y' && h[7] == (byte)'p';
        Guess("MP4/ISO BMFF", mp4, "offset 4 = ftyp");

        // Matroska/WebM: EBML 1A 45 DF A3
        bool mkv = read >= 4 && h[0] == 0x1A && h[1] == 0x45 && h[2] == 0xDF && h[3] == 0xA3;
        Guess("Matroska/WebM", mkv, "EBML header 1A 45 DF A3");

        // AVI: RIFF....AVI 
        bool avi = read >= 12 && h[0] == (byte)'R' && h[1] == (byte)'I' && h[2] == (byte)'F' && h[3] == (byte)'F' &&
                   h[8] == (byte)'A' && h[9] == (byte)'V' && h[10] == (byte)'I' && h[11] == (byte)' ';
        Guess("AVI", avi, "RIFF + AVI ");

        // FLV
        bool flv = read >= 3 && h[0] == (byte)'F' && h[1] == (byte)'L' && h[2] == (byte)'V';
        Guess("FLV", flv, "'FLV'");

        // Ogg
        bool ogg = read >= 4 && h[0] == (byte)'O' && h[1] == (byte)'g' && h[2] == (byte)'g' && h[3] == (byte)'S';
        Guess("Ogg", ogg, "'OggS'");

        // MPEG-TS: sync byte 0x47 at positions 0 and 188 and 376 (sample)
        bool ts = false;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> sample = stackalloc byte[188 * 3];
            var r = fs.Read(sample);
            if (r >= 188 * 2)
            {
                if (sample[0] == 0x47 && sample[188] == 0x47)
                    ts = true;
                if (r >= 188 * 3 && sample[0] == 0x47 && sample[188] == 0x47 && sample[376] == 0x47)
                    ts = true;
            }
        }
        catch { }
        Guess("MPEG-TS", ts, "sync byte 0x47 every 188 bytes");

        // MPEG-PS: 00 00 01 BA
        bool ps = read >= 4 && h[0] == 0x00 && h[1] == 0x00 && h[2] == 0x01 && h[3] == 0xBA;
        Guess("MPEG-PS", ps, "00 00 01 BA");

        // JPEG/PNG/GIF (for disguised files)
        bool jpg = read >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF;
        bool png = read >= 8 && h[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        bool gif = read >= 6 && (Encoding.ASCII.GetString(head, 0, 6) is "GIF87a" or "GIF89a");
        Guess("JPEG", jpg, "FF D8 FF");
        Guess("PNG", png, "89 50 4E 47...");
        Guess("GIF", gif, "GIF87a/GIF89a");

        // LLV magic (existing vault format) - just hint
        bool llv = read >= 4 && Encoding.ASCII.GetString(head, 0, 4) is "LLV1" or "LLV2";
        Guess("LLV(自定义加密)", llv, "Magic LLV1/LLV2");
    }

    private static void PrintEntropy(string path)
    {
        UI.PrintHeader("随机性/加密特征(粗略)");
        try
        {
            const int sampleSize = 256 * 1024;
            byte[] buf;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                buf = new byte[Math.Min(sampleSize, (int)Math.Min(int.MaxValue, fs.Length))];
                fs.ReadExactly(buf);
            }

            var entropy = ShannonEntropy(buf);
            UI.PrintResult("熵(0-8)", entropy.ToString("0.000", CultureInfo.InvariantCulture));
            UI.PrintResult("判断", entropy >= 7.5 ? "很高(疑似加密/高度压缩)" : entropy >= 6.5 ? "偏高(可能压缩/媒体编码)" : "一般");

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(buf);
            UI.PrintResult("SHA256(样本)", BitConverter.ToString(hash).Replace("-", ""));
        }
        catch (Exception ex)
        {
            UI.PrintError($"无法计算熵: {ex.Message}");
        }
    }

    private static void RunFfprobeIfAvailable(string path, bool raw)
    {
        if (!File.Exists(FfprobePath))
        {
            UI.PrintHeader("ffprobe 深度探测");
            UI.PrintInfo($"未找到 ffprobe.exe: {FfprobePath}");
            return;
        }

        UI.PrintHeader("ffprobe 深度探测");

        // 尽量输出多：format + streams + tags
        var args = $"-hide_banner -v error -show_format -show_streams -show_chapters -print_format json \"{path}\"";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = FfprobePath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null)
            {
                UI.PrintError("无法启动 ffprobe");
                return;
            }

            var json = p.StandardOutput.ReadToEnd();
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                UI.PrintError("ffprobe 识别失败 (该文件可能不是标准容器，或是加密/前置自定义头)");
                if (!string.IsNullOrWhiteSpace(err))
                    UI.PrintError(err.Trim());
                return;
            }

            PrintFfprobeSummary(json);

            if (raw)
            {
                Console.WriteLine();
                UI.PrintHeader("ffprobe 原始 JSON (--raw)");
                Console.WriteLine(json);
            }
        }
        catch (Exception ex)
        {
            UI.PrintError($"ffprobe 执行异常: {ex.Message}");
        }
    }

    private static void PrintFfprobeSummary(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            UI.PrintHeader("ffprobe 解析(中文摘要)");

            if (root.TryGetProperty("format", out var format))
            {
                var fmtName = format.TryGetProperty("format_name", out var v1) ? v1.GetString() : null;
                var fmtLong = format.TryGetProperty("format_long_name", out var v2) ? v2.GetString() : null;
                var duration = format.TryGetProperty("duration", out var v3) ? v3.GetString() : null;
                var size = format.TryGetProperty("size", out var v4) ? v4.GetString() : null;
                var bitRate = format.TryGetProperty("bit_rate", out var v5) ? v5.GetString() : null;

                UI.PrintResult("封装格式", string.IsNullOrWhiteSpace(fmtLong) ? (fmtName ?? "(未知)") : $"{fmtLong} ({fmtName})");
                if (double.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out var ds))
                    UI.PrintResult("总时长", TimeSpan.FromSeconds(ds).ToString(@"hh\:mm\:ss"));
                if (long.TryParse(size, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
                    UI.PrintResult("容器大小", Utils.FormatSize(bytes));
                if (long.TryParse(bitRate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var br) && br > 0)
                    UI.PrintResult("总码率", $"{(br / 1000.0).ToString("0", CultureInfo.InvariantCulture)} kbps");
            }

            if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
            {
                int vCount = 0, aCount = 0, sCount = 0;
                foreach (var s in streams.EnumerateArray())
                {
                    var type = s.TryGetProperty("codec_type", out var t) ? t.GetString() : "";
                    if (type == "video") vCount++;
                    else if (type == "audio") aCount++;
                    else if (type == "subtitle") sCount++;
                }
                UI.PrintResult("流数量", $"视频 {vCount}, 音频 {aCount}, 字幕 {sCount}, 总计 {streams.GetArrayLength()}");

                int idx = 0;
                foreach (var s in streams.EnumerateArray())
                {
                    idx++;
                    var type = s.TryGetProperty("codec_type", out var t) ? t.GetString() : "unknown";
                    var codec = s.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;
                    var codecLong = s.TryGetProperty("codec_long_name", out var cl) ? cl.GetString() : null;
                    var profile = s.TryGetProperty("profile", out var pr) ? pr.GetString() : null;
                    var bitRate = s.TryGetProperty("bit_rate", out var br) ? br.GetString() : null;

                    Console.WriteLine();
                    UI.PrintHeader($"Stream #{idx} ({type})");
                    UI.PrintResult("编码", string.IsNullOrWhiteSpace(codecLong) ? (codec ?? "(未知)") : $"{codecLong} ({codec})");
                    if (!string.IsNullOrWhiteSpace(profile)) UI.PrintResult("Profile", profile);
                    if (long.TryParse(bitRate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) && b > 0)
                        UI.PrintResult("码率", $"{(b / 1000.0).ToString("0", CultureInfo.InvariantCulture)} kbps");

                    if (type == "video")
                    {
                        var w = s.TryGetProperty("width", out var wv) ? wv.GetInt32() : 0;
                        var h = s.TryGetProperty("height", out var hv) ? hv.GetInt32() : 0;
                        if (w > 0 && h > 0) UI.PrintResult("分辨率", $"{w}x{h}");

                        var fps = TryGetFps(s);
                        if (fps > 0) UI.PrintResult("帧率", $"{fps.ToString("0.###", CultureInfo.InvariantCulture)} fps");

                        var pix = s.TryGetProperty("pix_fmt", out var pv) ? pv.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(pix)) UI.PrintResult("像素格式", pix);

                        var dar = s.TryGetProperty("display_aspect_ratio", out var dav) ? dav.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(dar) && dar != "0:0") UI.PrintResult("显示宽高比", dar);

                        var sar = s.TryGetProperty("sample_aspect_ratio", out var sav) ? sav.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(sar) && sar != "0:1") UI.PrintResult("像素宽高比", sar);
                    }
                    else if (type == "audio")
                    {
                        var sr = s.TryGetProperty("sample_rate", out var srv) ? srv.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(sr)) UI.PrintResult("采样率", $"{sr} Hz");

                        var ch = s.TryGetProperty("channels", out var chv) ? chv.GetInt32() : 0;
                        if (ch > 0) UI.PrintResult("声道", ch.ToString(CultureInfo.InvariantCulture));

                        var layout = s.TryGetProperty("channel_layout", out var lv) ? lv.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(layout)) UI.PrintResult("声道布局", layout);

                        var fmt = s.TryGetProperty("sample_fmt", out var fv) ? fv.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(fmt)) UI.PrintResult("采样格式", fmt);
                    }

                    // tags（常见：language / title）
                    if (s.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in tags.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.String)
                            {
                                var val = prop.Value.GetString();
                                if (!string.IsNullOrWhiteSpace(val))
                                    UI.PrintResult($"tag:{prop.Name}", val);
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            UI.PrintError("ffprobe JSON 解析失败(输出结构可能变化)。可加 --raw 查看原始 JSON。");
        }
    }

    private static double TryGetFps(JsonElement stream)
    {
        // 优先用 avg_frame_rate，其次 r_frame_rate
        string? afr = stream.TryGetProperty("avg_frame_rate", out var a) ? a.GetString() : null;
        var fps = ParseRatio(afr);
        if (fps > 0) return fps;
        string? rfr = stream.TryGetProperty("r_frame_rate", out var r) ? r.GetString() : null;
        return ParseRatio(rfr);
    }

    private static double ParseRatio(string? ratio)
    {
        if (string.IsNullOrWhiteSpace(ratio)) return 0;
        var parts = ratio.Split('/');
        if (parts.Length != 2) return 0;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return 0;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return 0;
        if (d <= 0) return 0;
        return n / d;
    }

    private static double ShannonEntropy(byte[] data)
    {
        if (data.Length == 0) return 0;
        Span<int> counts = stackalloc int[256];
        foreach (var b in data) counts[b]++;

        double ent = 0;
        double inv = 1.0 / data.Length;
        for (int i = 0; i < 256; i++)
        {
            if (counts[i] == 0) continue;
            double p = counts[i] * inv;
            ent -= p * Math.Log(p, 2);
        }
        return ent;
    }

    private static string ToPrintableAscii(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            char c = (char)b;
            sb.Append(c is >= ' ' and <= '~' ? c : '.');
        }
        return sb.ToString();
    }

    private static string EscapeNonPrintable(string s)
    {
        var sb = new StringBuilder(s.Length * 2);
        foreach (var ch in s)
        {
            if (ch is >= ' ' and <= '~') sb.Append(ch);
            else sb.Append("\\u").Append(((int)ch).ToString("X4"));
        }
        return sb.ToString();
    }
}
