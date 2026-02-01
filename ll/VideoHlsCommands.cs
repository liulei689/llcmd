using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LL;

// 经验总结后的简化版：
// - 只支持本地 HLS AES-128（.m3u8 + .ts + key.key）
// - encrypt: 自动扫描文件/文件夹，输出到 <outRoot>/<name>/index.m3u8
// - play: 直接播放 m3u8（或目录下 index.m3u8），必要时修复 BOM/URI，并把 key 覆盖为用户输入
public static class VideoHlsCommands
{
    private static readonly string[] VideoExts = [".mp4", ".mkv", ".avi", ".mov", ".flv", ".wmv", ".webm", ".m4v"];

    private static string ToolsBin => Path.Combine(AppContext.BaseDirectory, "tools", "bin");
    private static string DefaultFfmpeg => Path.Combine(ToolsBin, "ffmpeg.exe");
    private static string DefaultFfplay => Path.Combine(ToolsBin, "ffplay.exe");

    public static void Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            PrintHelp();
            return;
        }

        var sub = args[0].Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        switch (sub)
        {
            case "enc":
            case "encrypt":
                Encrypt(rest);
                return;
            case "play":
                Play(rest);
                return;
            default:
                UI.PrintError($"未知子命令: {args[0]}");
                PrintHelp();
                return;
        }
    }

    private static (string? input, string? key, string? outDir, int segSeconds, bool autoExit) ParseArgs(string mode, string[] args)
    {
        string? input = null;
        string? key = null;
        string? outDir = null;
        int segSeconds = 6;
        bool autoExit = false;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--in" or "-i")
            {
                if (i + 1 < args.Length) input = args[++i];
            }
            else if (a is "--key" or "--pwd" or "-k")
            {
                if (i + 1 < args.Length) key = args[++i];
            }
            else if (a is "--out" or "-o")
            {
                if (i + 1 < args.Length) outDir = args[++i];
            }
            else if (a is "--seg" && i + 1 < args.Length && int.TryParse(args[i + 1], out var s))
            {
                segSeconds = Math.Clamp(s, 1, 30);
                i++;
            }
            else if (a is "--autoexit")
            {
                autoExit = true;
            }
        }

        // 兼容旧位置参数
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(key))
        {
            if (mode is "enc" or "encrypt")
            {
                if (args.Length >= 1 && string.IsNullOrWhiteSpace(input)) input = args[0];
                if (args.Length >= 2 && string.IsNullOrWhiteSpace(key)) key = args[1];
                if (args.Length >= 3 && string.IsNullOrWhiteSpace(outDir)) outDir = args[2];
            }
            else if (mode is "play")
            {
                if (args.Length >= 1 && string.IsNullOrWhiteSpace(input)) input = args[0];
                if (args.Length >= 2 && string.IsNullOrWhiteSpace(key)) key = args[1];
            }
        }

        return (input, key, outDir, segSeconds, autoExit);
    }

    private static void PrintHelp()
    {
        UI.PrintInfo(
            "video hls (HLS AES-128)\n" +
            "  video hls enc --in <file|folder> --key <key> [--out <dir>] [--seg 6]\n" +
            "  video hls play --in <m3u8|folder> --key <key> [--autoexit]\n" +
            "(兼容旧位置参数用法)\n" +
            "输出: index.m3u8 + seg_*.ts + key.key\n" +
            "说明: 播放使用 ffplay 自动解密；会自动清理 m3u8 BOM/隐藏字符并修正 URI。"
        );
    }

    private static string? ResolveExe(string preferred, string exeName)
    {
        if (File.Exists(preferred)) return preferred;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = exeName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null) return null;
            var outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) return null;
            var path = outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path.Trim();
        }
        catch { }

        return null;
    }

    private static string[] EnumerateVideos(string input)
    {
        if (File.Exists(input))
        {
            var ext = Path.GetExtension(input);
            return VideoExts.Contains(ext, StringComparer.OrdinalIgnoreCase) ? [input] : [];
        }

        if (Directory.Exists(input))
        {
            return Directory.GetFiles(input, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => VideoExts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return [];
    }

    private static void Encrypt(string[] args)
    {
        var p = ParseArgs("enc", args);
        if (string.IsNullOrWhiteSpace(p.input) || string.IsNullOrWhiteSpace(p.key))
        {
            UI.PrintError("用法: video hls enc --in <file|folder> --key <key> [--out <dir>] [--seg 6]");
            UI.PrintInfo("兼容: video hls enc <file|folder> <key> [outDir]");
            return;
        }

        var src = p.input!;
        var keyText = p.key!;
        var outRoot = !string.IsNullOrWhiteSpace(p.outDir)
            ? p.outDir!
            : (Directory.Exists(src)
                ? Path.Combine(src, "hls")
                : Path.Combine(Path.GetDirectoryName(src) ?? ".", "hls"));

        var ffmpegExe = ResolveExe(DefaultFfmpeg, "ffmpeg.exe");
        if (string.IsNullOrEmpty(ffmpegExe))
        {
            UI.PrintError($"未找到 ffmpeg.exe: {DefaultFfmpeg} (且 PATH 中也未找到)");
            return;
        }

        var videos = EnumerateVideos(src);
        if (videos.Length == 0)
        {
            UI.PrintError($"未找到视频: {src}");
            return;
        }

        Directory.CreateDirectory(outRoot);

        var key16 = DeriveAes128Key(keyText);
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        UI.PrintInfo($"开始加密: {videos.Length} 个文件 -> {outRoot}");

        for (int i = 0; i < videos.Length; i++)
        {
            var v = videos[i];
            var name = Path.GetFileNameWithoutExtension(v);
            var outDir = Path.Combine(outRoot, name);
            Directory.CreateDirectory(outDir);

            var m3u8 = Path.Combine(outDir, "index.m3u8");
            var keyFile = Path.Combine(outDir, "key.key");
            var keyInfo = Path.Combine(outDir, "keyinfo.txt");
            var segPattern = Path.Combine(outDir, "seg_%05d.ts");

            File.WriteAllBytes(keyFile, key16);
            // keyinfo 规则：
            //  1) 第一行: 写入 m3u8 的 URI（尽量相对，便于搬移目录）
            //  2) 第二行: ffmpeg 读取 key 的本地路径（必须可被当前工作目录解析；稳妥用绝对路径）
            File.WriteAllText(keyInfo, "key.key\n" + keyFile, utf8NoBom);

            UI.PrintInfo($"[{i + 1}/{videos.Length}] {Path.GetFileName(v)}");

            // 说明：默认不强制转码，交给 ffmpeg 自己决定（多数情况下会 copy 或轻微重封装）。
            // 为了稳定性，这里用 mpegts 分片输出。
            // 让 ffmpeg 在 outDir 下生成文件，避免相对路径错乱
            var m3u8Rel = "index.m3u8";
            var segRel = "seg_%05d.ts";
            var cmd = $"-hide_banner -y -i \"{v}\" " +
                      "-hls_playlist_type vod " +
                      "-hls_segment_type mpegts " +
                      $"-hls_time {p.segSeconds.ToString(CultureInfo.InvariantCulture)} " +
                      $"-hls_segment_filename \"{segRel}\" " +
                      $"-hls_key_info_file \"{keyInfo}\" " +
                      $"\"{m3u8Rel}\"";

            if (!Run(ffmpegExe, cmd, out var errTail, workingDir: outDir))
            {
                UI.PrintError($"失败(跳过): {v}");
                if (!string.IsNullOrWhiteSpace(errTail)) UI.PrintError(errTail);
                continue;
            }

            // 修复可能的 BOM/URI
            FixM3u8(m3u8);
            UI.PrintSuccess($"完成: {m3u8}");
        }

        UI.PrintSuccess("加密完成。");
    }

    private static void Play(string[] args)
    {
        var parsed = ParseArgs("play", args);
        if (string.IsNullOrWhiteSpace(parsed.input) || string.IsNullOrWhiteSpace(parsed.key))
        {
            UI.PrintError("用法: video hls play --in <m3u8|folder> --key <key> [--autoexit]");
            UI.PrintInfo("兼容: video hls play <m3u8|folder> <key>");
            return;
        }

        var input = parsed.input!;
        var keyText = parsed.key!;

        var ffplayExe = ResolveExe(DefaultFfplay, "ffplay.exe");
        if (string.IsNullOrEmpty(ffplayExe))
        {
            UI.PrintError($"未找到 ffplay.exe: {DefaultFfplay} (且 PATH 中也未找到)");
            return;
        }

        string m3u8;
        if (File.Exists(input) && input.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            m3u8 = input;
        }
        else if (Directory.Exists(input))
        {
            m3u8 = Path.Combine(input, "index.m3u8");
            if (!File.Exists(m3u8))
            {
                UI.PrintError($"目录内未找到 index.m3u8: {input}");
                return;
            }
        }
        else
        {
            UI.PrintError($"输入不存在: {input}");
            return;
        }

        FixM3u8(m3u8);

        var dir = Path.GetDirectoryName(m3u8) ?? ".";
        var keyFile = Path.Combine(dir, "key.key");
        if (!File.Exists(keyFile))
        {
            // 兼容旧数据
            var old = Path.Combine(dir, "key.bin");
            if (File.Exists(old))
            {
                try { File.Move(old, keyFile, overwrite: true); } catch { }
            }
        }

        if (!File.Exists(keyFile))
        {
            UI.PrintError($"找不到 key 文件: {keyFile}");
            return;
        }

        // 覆盖 key（避免用户输入 key 和文件不一致）
        var bak = keyFile + ".bak";
        var key16 = DeriveAes128Key(keyText);

        try
        {
            try { File.Copy(keyFile, bak, true); } catch { }
            File.WriteAllBytes(keyFile, key16);

            var auto = parsed.autoExit ? "-autoexit " : string.Empty;
            // ffplay 不一定有进度条 UI，但 -stats 会持续输出播放进度
            var argsPlay = $"{auto}-stats -loglevel warning -allowed_extensions ALL -i \"{m3u8}\"";
            UI.PrintInfo($"启动 ffplay: {ffplayExe} {argsPlay}");

            var psi = new ProcessStartInfo
            {
                FileName = ffplayExe,
                Arguments = argsPlay,
                UseShellExecute = false,
                RedirectStandardError = false,
                RedirectStandardOutput = false,
                CreateNoWindow = false
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                UI.PrintError("启动 ffplay 失败");
                return;
            }

            proc.WaitForExit();
            if (proc.ExitCode != 0)
                UI.PrintError($"ffplay 退出码: {proc.ExitCode}");
        }
        finally
        {
            try
            {
                if (File.Exists(bak))
                {
                    File.Copy(bak, keyFile, true);
                    File.Delete(bak);
                }
            }
            catch { }
        }
    }

    private static void FixM3u8(string m3u8Path)
    {
        try
        {
            var text = File.ReadAllText(m3u8Path);
            var cleaned = text
                .Replace("\uFEFF", string.Empty)
                .Replace("\u200B", string.Empty)
                .Replace("URI=\"key.bin\"", "URI=\"key.key\"")
                .Replace("URI=\"key.key\"", "URI=\"key.key\"");

            if (!string.Equals(text, cleaned, StringComparison.Ordinal))
                File.WriteAllText(m3u8Path, cleaned, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch { }
    }

    private static byte[] DeriveAes128Key(string keyText)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(keyText));
        return hash.Take(16).ToArray();
    }

    private static bool Run(string exe, string args, out string? errTail, string? workingDir = null)
    {
        errTail = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!string.IsNullOrWhiteSpace(workingDir))
                psi.WorkingDirectory = workingDir;

            using var p = Process.Start(psi);
            if (p == null) return false;

            _ = p.StandardOutput.ReadToEndAsync();
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (!string.IsNullOrWhiteSpace(err))
                errTail = err.Length <= 1200 ? err : err[^1200..];

            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
