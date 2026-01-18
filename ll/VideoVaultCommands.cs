using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Sockets;

namespace LL;

public static class VideoVaultCommands
{
    private static readonly byte[] MagicV1 = Encoding.ASCII.GetBytes("LLV1");
    private static readonly byte[] MagicV2 = Encoding.ASCII.GetBytes("LLV2");

    private const int DefaultChunkSize = 1024 * 1024; // 1MB
    private const int MaxHintBytes = 200;
    private const ushort FlagHintUtf16 = 1;

    // Format:
    // [4]  Magic "LLV1"
    // [1]  flags (0)
    // [16] salt
    // [12] nonce
    // [8]  originalLength (Int64)
    // [..] ciphertext
    // [16] tag

    public static void Encrypt(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            PrintEncryptHelp();
            return;
        }

        string input = args[0];
        string? outPath = null;
        string? password = null;
        bool recursive = false;
        int chunkSize = DefaultChunkSize;
        string? hint = null;
        bool askHint = false;

        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--out=", StringComparison.OrdinalIgnoreCase)) outPath = a["--out=".Length..].Trim('"');
            else if (a.StartsWith("--pwd=", StringComparison.OrdinalIgnoreCase)) password = a["--pwd=".Length..];
            else if (a is "-r" or "--recursive") recursive = true;
            else if (a.StartsWith("--chunk=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a["--chunk=".Length..], out var cs))
                chunkSize = Math.Clamp(cs, 64 * 1024, 16 * 1024 * 1024);
            else if (a.StartsWith("--hint=", StringComparison.OrdinalIgnoreCase)) hint = a["--hint=".Length..];
            else if (a is "--hint") askHint = true;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            password = ReadPassword("请输入密码: ");
            var confirm = ReadPassword("再次确认: ");
            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                UI.PrintError("两次密码不一致。");
                return;
            }
        }

        // Hint is optional. For batch jobs, prompt once.
        if (string.IsNullOrWhiteSpace(hint) && askHint)
        {
            Console.Write("密码提示(可空, 最多 100 字): ");
            hint = Console.ReadLine();
        }


        var inputs = ExpandInputs(input, recursive);
        if (inputs.Count == 0)
        {
            UI.PrintError($"未找到文件: {input}");
            return;
        }

        UI.PrintHeader("视频加密");
        UI.PrintResult("文件数", inputs.Count.ToString("n0"));
        long totalBytes = 0;
        foreach (var f in inputs)
        {
            try { totalBytes += new FileInfo(f).Length; } catch { }
        }
        UI.PrintResult("总大小", Utils.FormatSize(totalBytes));
        UI.PrintResult("输出", string.IsNullOrWhiteSpace(outPath) ? "同目录" : outPath);
        UI.PrintInfo("开始加密...");
        Console.WriteLine();

        var swAll = Stopwatch.StartNew();
        int ok = 0;
        int fail = 0;
        long doneBytes = 0;

        foreach (var file in inputs)
        {
            try
            {
                var dst = ResolveOutputPath(file, outPath);
                var fi = new FileInfo(file);
                EncryptOneV2(file, dst, password, chunkSize, hint);
                ok++;
                doneBytes += fi.Length;

                var elapsed = swAll.Elapsed;
                var speed = elapsed.TotalSeconds > 0.1 ? doneBytes / elapsed.TotalSeconds : 0;
                var remainBytes = Math.Max(0, totalBytes - doneBytes);
                var eta = speed > 1 ? TimeSpan.FromSeconds(remainBytes / speed) : TimeSpan.Zero;
                UI.PrintResult("进度", $"{ok + fail}/{inputs.Count}  已完成 {Utils.FormatSize(doneBytes)} / {Utils.FormatSize(totalBytes)}  预计剩余 {eta:hh\\:mm\\:ss}");
                UI.PrintSuccess($"已加密: {file} -> {dst}");
            }
            catch (Exception ex)
            {
                fail++;
                UI.PrintError($"加密失败: {file} ({ex.Message})");
            }
        }

        swAll.Stop();
        Console.WriteLine();
        UI.PrintSuccess($"完成: 成功 {ok:n0}, 失败 {fail:n0}, 用时 {swAll.Elapsed:hh\\:mm\\:ss}");
    }

    private static string? ReadLineUtf8()
    {
        try
        {
            using var sr = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8, false, 1024, leaveOpen: true);
            return sr.ReadLine();
        }
        catch
        {
            return Console.ReadLine();
        }
    }
    public static void Play(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            PrintPlayHelp();
            return;
        }

        var target = args[0];
        if (Directory.Exists(target))
        {
            PlayDirectorySession(target, args.Skip(1).ToArray());
            return;
        }

        var file = ResolveLlvFileFromArgument(target);
        if (file is null) return;

        string? password = null;
        bool autoDelete = false;
        bool useHtml5 = false;
        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--pwd=", StringComparison.OrdinalIgnoreCase)) password = a["--pwd=".Length..];
            else if (a is "--autodel") autoDelete = true;
            else if (a is "--html5") useHtml5 = true;
        }
        if (string.IsNullOrWhiteSpace(password))
            password = ReadPassword("请输入密码: ");

        while (true)
        {
            if (PlayOne(file, password, autoDelete, useHtml5))
                return;
            password = ReadPassword("密码错误，请重新输入(回车或输入q退出): ");
            if (string.IsNullOrWhiteSpace(password) || password.Trim().Equals("q", StringComparison.OrdinalIgnoreCase) || password.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                UI.PrintInfo("已取消播放。");
                return;
            }
        }
    }

    public static void CleanTemp(string[] args)
    {
        if (args.Length > 0 && (args[0] is "help" or "-h" or "--help"))
        {
            PrintCleanHelp();
            return;
        }

        var dir = GetTempDir();
        if (!Directory.Exists(dir))
        {
            UI.PrintInfo("临时目录为空。");
            return;
        }

        int files = 0;
        long bytes = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*.*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false }))
        {
            try
            {
                var fi = new FileInfo(f);
                bytes += fi.Length;
                files++;
            }
            catch { }
        }

        bool force = args.Any(a => a is "-y" or "--yes");
        UI.PrintHeader("清理临时解密目录");
        UI.PrintResult("路径", dir);
        UI.PrintResult("文件", files.ToString("n0"));
        UI.PrintResult("大小", Utils.FormatSize(bytes));

        if (!force)
        {
            Console.Write("确认删除? (y/N): ");
            var s = Console.ReadLine();
            if (!string.Equals(s, "y", StringComparison.OrdinalIgnoreCase))
            {
                UI.PrintInfo("已取消。");
                return;
            }
        }

        try
        {
            Directory.Delete(dir, recursive: true);
            UI.PrintSuccess("已清理。");
        }
        catch (Exception ex)
        {
            UI.PrintError($"清理失败: {ex.Message}");
        }
    }

    private static void PlayDirectorySession(string dir, string[] args)
    {
        var files = EnumerateLlvFiles(dir, recursive: false).ToArray();
        if (files.Length == 0)
        {
            UI.PrintError("目录中没有 .llv 文件。");
            return;
        }

        string? password = null;
        bool autoDelete = false;
        bool useHtml5 = false;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--pwd=", StringComparison.OrdinalIgnoreCase)) password = a["--pwd=".Length..];
            else if (a is "--autodel") autoDelete = true;
            else if (a is "--html5") useHtml5 = true;
        }
        if (string.IsNullOrWhiteSpace(password))
            password = ReadPassword("请输入密码(本次会话仅输入一次): ");

        bool printedList = false;
        while (true)
        {
            if (!printedList)
            {
                Console.WriteLine();
                UI.PrintHeader($"播放列表: {Path.GetFullPath(dir)}");
                for (int i = 0; i < files.Length; i++)
                {
                    Console.WriteLine($"[{i + 1,3}] {Path.GetFileName(files[i])}");
                }
                Console.WriteLine("[  0] 退出(自动清理临时目录)");
                printedList = true;
            }
            Console.Write("请输入序号: ");
            var s = Console.ReadLine();
            if (!int.TryParse(s, out var idx))
            {
                UI.PrintError("输入无效。");
                continue;
            }
            if (idx == 0)
            {
                // silent cleanup
                try
                {
                    var tempDir = GetTempDir();
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
                catch { }
                return;
            }
            if (idx < 1 || idx > files.Length)
            {
                UI.PrintError("输入无效。");
                continue;
            }

            var file = files[idx - 1];
            // Try current session password first.
            if (PlayOne(file, password, autoDelete, useHtml5))
                continue;

            // This file may use a different password; prompt retry without re-printing list.
            while (true)
            {
                password = ReadPassword($"密码错误(文件: {Path.GetFileName(file)}), 请重新输入: ");
                if (PlayOne(file, password, autoDelete, useHtml5))
                    break;
            }
        }
    }

    private static bool PlayOne(string file, string password, bool autoDelete, bool useHtml5 = false)
    {
        try
        {
            var tempDir = GetTempDir();
            Directory.CreateDirectory(tempDir);
            var tempFile = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(file) + "_" + Guid.NewGuid().ToString("N") + ".mp4");

            DecryptToFile(file, tempFile, password);
            UI.PrintSuccess($"已解密到临时文件: {tempFile}");

            if (useHtml5)
            {
                // Start local HTTP server for HTML5 video player
                int port = GetFreePort();
                _ = Task.Run(() => RunVideoServer(tempFile, port)); // Run in background
                Thread.Sleep(500); // Allow server to start

                Process.Start(new ProcessStartInfo
                {
                    FileName = $"http://localhost:{port}/",
                    UseShellExecute = true
                });

                // Do not wait; let server run in background
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = tempFile,
                    UseShellExecute = true
                });
            }

            if (autoDelete)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // give the player a moment to open the file
                        await Task.Delay(2000);
                        TryDeleteWithRetry(tempFile);
                    }
                    catch { }
                });
            }
            else
            {
                UI.PrintInfo("提示: 临时文件位于 %TEMP%/llv。可用 clrv 清理。");
            }

            return true;
        }
        catch (CryptographicException)
        {
            // Show hint only on password/crypto failure.
            var hint = TryReadHintFromFile(file);
            if (!string.IsNullOrWhiteSpace(hint))
            {
                UI.PrintInfo("密码提示:");
                PrintTextSafe(hint);
            }
            UI.PrintError("密码错误或文件已损坏。");
            return false;
        }
        catch (Exception ex)
        {
            UI.PrintError($"播放失败: {ex.Message}");
            return true;
        }
    }

    private static void PrintTextSafe(string text)
    {
        try
        {
            Console.WriteLine(text);
        }
        catch
        {
            // Fallback: unicode escapes, readable in any console.
            var sb = new StringBuilder(text.Length * 6);
            foreach (var ch in text)
            {
                if (ch >= 32 && ch <= 126)
                    sb.Append(ch);
                else
                    sb.Append("\\u").Append(((int)ch).ToString("X4"));
            }
            Console.WriteLine(sb.ToString());
        }
    }

    private static string? TryReadHintFromFile(string file)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> magic = stackalloc byte[4];
            if (fs.Read(magic) != 4) return null;

            // Only LLV2 has hint.
            if (!magic.SequenceEqual(MagicV2)) return null;

            int flagsByte = fs.ReadByte();
            if (flagsByte < 0) return null;
            bool hintUtf16 = (flagsByte & FlagHintUtf16) != 0;

            fs.Position += 16; // salt
            fs.Position += 12; // baseNonce
            fs.Position += 8;  // originalLength
            fs.Position += 4;  // chunkSize
            fs.Position += 4;  // chunks

            Span<byte> len2 = stackalloc byte[2];
            fs.ReadExactly(len2);
            ushort hintLen = BitConverter.ToUInt16(len2);
            if (hintLen == 0) return null;

            if (hintUtf16)
            {
                // hintLen = char count
                int byteCount = hintLen * 2;
                if (byteCount > 4096) byteCount = 4096; // sanity guard
                byte[] buf = new byte[byteCount];
                fs.ReadExactly(buf);
                if (buf.Length > MaxHintBytes)
                    buf = buf.AsSpan(0, MaxHintBytes).ToArray();
                return Encoding.Unicode.GetString(buf).TrimEnd('\0').Trim();
            }
            else
            {
                // legacy UTF-8 byte length
                int bytesToRead = Math.Min((int)hintLen, 4096);
                byte[] buf = new byte[bytesToRead];
                fs.ReadExactly(buf);
                buf = TrimToValidUtf8(buf, MaxHintBytes);
                if (buf.Length == 0) return null;
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
                return utf8.GetString(buf).Trim();
            }
        }
        catch
        {
            return null;
        }
    }

    private static string GetTempDir() => Path.Combine(Path.GetTempPath(), "llv");

    private static void TryDeleteWithRetry(string path)
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                if (!File.Exists(path)) return;
                File.Delete(path);
                return;
            }
            catch
            {
                Thread.Sleep(500);
            }
        }
    }

    public static void List(string[] args)
    {
        if (args.Length > 0 && (args[0] is "help" or "-h" or "--help"))
        {
            PrintListHelp();
            return;
        }

        string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        bool recursive = args.Any(a => a is "-r" or "--recursive");

        if (!Directory.Exists(root))
        {
            UI.PrintError($"目录不存在: {root}");
            return;
        }

        var files = EnumerateLlvFiles(root, recursive).ToArray();
        UI.PrintHeader("加密视频列表 (.llv)");
        UI.PrintResult("目录", Path.GetFullPath(root));
        UI.PrintResult("数量", files.Length.ToString("n0"));
        Console.WriteLine();

        if (files.Length == 0)
        {
            UI.PrintInfo("未找到 .llv 文件。");
            return;
        }

        for (int i = 0; i < files.Length; i++)
        {
            Console.WriteLine($"[{i + 1,3}] {files[i]}");
        }
    }

    private static void PrintCleanHelp()
    {
        UI.PrintHeader("clrv - 清理临时解密目录");
        UI.PrintInfo("用法: clrv [-y]");
        Console.WriteLine();
        UI.PrintResult("clrv", "交互确认后删除 %TEMP%/llv");
        UI.PrintResult("clrv -y", "直接删除，不提示");
    }

    private static byte[] TrimToValidUtf8(byte[] bytes, int maxBytes)
    {
        int len = Math.Min(bytes.Length, maxBytes);
        while (len > 0)
        {
            byte b = bytes[len - 1];
            if ((b & 0b1000_0000) == 0)
                break;

            int cont = 0;
            int i = len - 1;
            while (i >= 0 && (bytes[i] & 0b1100_0000) == 0b1000_0000)
            {
                cont++;
                i--;
            }

            if (i < 0)
            {
                len = 0;
                break;
            }

            byte lead = bytes[i];
            int needed = lead switch
            {
                >= 0b1111_0000 and < 0b1111_1000 => 3,
                >= 0b1110_0000 and < 0b1111_0000 => 2,
                >= 0b1100_0000 and < 0b1110_0000 => 1,
                _ => 0
            };

            if (needed == cont)
                break;

            len = i;
        }

        if (len <= 0) return Array.Empty<byte>();
        return bytes.AsSpan(0, len).ToArray();
    }

    private static void EncryptOneV2(string inputFile, string outputFile, string password, int chunkSize, string? hint)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);

        var salt = RandomNumberGenerator.GetBytes(16);
        var baseNonce = RandomNumberGenerator.GetBytes(12);
        var key = DeriveKey(password, salt);

        var info = new FileInfo(inputFile);
        long originalLength = info.Length;
        int chunks = (int)((originalLength + chunkSize - 1) / chunkSize);

        using var input = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None);

        // Header
        output.Write(MagicV2);
        byte flags = 0;
        // store hint as UTF-16LE for robust Chinese display
        flags |= (byte)FlagHintUtf16;
        output.WriteByte(flags);
        output.Write(salt);
        output.Write(baseNonce);
        output.Write(BitConverter.GetBytes(originalLength));
        output.Write(BitConverter.GetBytes(chunkSize));
        output.Write(BitConverter.GetBytes(chunks));

        // Optional hint (length-prefixed)
        ushort hintCharCount = 0;
        byte[] hintBytes = Array.Empty<byte>();
        if (!string.IsNullOrWhiteSpace(hint))
        {
            // Always encode as UTF-16LE
            var bytes = Encoding.Unicode.GetBytes(hint);
            int maxBytes = MaxHintBytes;
            if (bytes.Length > maxBytes)
                bytes = bytes.AsSpan(0, maxBytes).ToArray();
            hintCharCount = (ushort)(bytes.Length / 2);
            hintBytes = bytes;
        }
        output.Write(BitConverter.GetBytes(hintCharCount));
        if (hintBytes.Length > 0)
            output.Write(hintBytes, 0, hintBytes.Length);

        // Reserve tag table (16 bytes per chunk) AFTER hint
        long tagTableOffset = output.Position;
        output.Position += (long)chunks * 16;

        var tags = new byte[chunks * 16];
        byte[] plainBuf = new byte[chunkSize];
        byte[] cipherBuf = new byte[chunkSize];

        using var aes = new AesGcm(key, 16);
        for (int i = 0; i < chunks; i++)
        {
            int read = ReadAtMost(input, plainBuf);
            if (read <= 0) break;

            Span<byte> nonce = stackalloc byte[12];
            DeriveChunkNonce(baseNonce, i, nonce);

            Span<byte> tag = tags.AsSpan(i * 16, 16);
            aes.Encrypt(nonce, plainBuf.AsSpan(0, read), cipherBuf.AsSpan(0, read), tag);
            output.Write(cipherBuf, 0, read);
        }

        // Write tag table
        long endPos = output.Position;
        output.Position = tagTableOffset;
        output.Write(tags);
        output.Position = endPos;

        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(plainBuf);
        CryptographicOperations.ZeroMemory(cipherBuf);
        CryptographicOperations.ZeroMemory(tags);
    }

    private static void DecryptToFile(string inputFile, string outputFile, string password)
    {
        using var fs = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[4];
        if (fs.Read(header) != 4)
            throw new InvalidDataException("文件头损坏。");

        if (header.SequenceEqual(MagicV2))
        {
            DecryptV2ToFile(fs, outputFile, password);
            return;
        }

        if (header.SequenceEqual(MagicV1))
        {
            DecryptV1ToFile(fs, outputFile, password);
            return;
        }

        throw new InvalidDataException("不是有效的 .llv 文件。");
    }

    private static void DecryptV1ToFile(FileStream fs, string outputFile, string password)
    {
        int flags = fs.ReadByte();
        if (flags < 0) throw new InvalidDataException("文件头损坏。");

        byte[] salt = new byte[16];
        byte[] nonce = new byte[12];
        fs.ReadExactly(salt);
        fs.ReadExactly(nonce);

        Span<byte> lenBuf = stackalloc byte[8];
        fs.ReadExactly(lenBuf);
        long plainLen = BitConverter.ToInt64(lenBuf);
        if (plainLen < 0 || plainLen > int.MaxValue)
            throw new InvalidDataException("文件长度异常。");

        long remaining = fs.Length - fs.Position;
        if (remaining < 16)
            throw new InvalidDataException("文件损坏。");

        int cipherLen = checked((int)(remaining - 16));
        byte[] ciphertext = new byte[cipherLen];
        fs.ReadExactly(ciphertext);
        byte[] tag = new byte[16];
        fs.ReadExactly(tag);

        byte[] plaintext = new byte[cipherLen];
        var key = DeriveKey(password, salt);
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        File.WriteAllBytes(outputFile, plaintext);

        CryptographicOperations.ZeroMemory(plaintext);
        CryptographicOperations.ZeroMemory(key);
    }

    private static void DecryptV2ToFile(FileStream fs, string outputFile, string password)
    {
        int flagsByte = fs.ReadByte();
        if (flagsByte < 0) throw new InvalidDataException("文件头损坏。");
        bool hintUtf16 = (flagsByte & FlagHintUtf16) != 0;

        byte[] salt = new byte[16];
        byte[] baseNonce = new byte[12];
        fs.ReadExactly(salt);
        fs.ReadExactly(baseNonce);

        Span<byte> buf8 = stackalloc byte[8];
        fs.ReadExactly(buf8);
        long originalLength = BitConverter.ToInt64(buf8);
        if (originalLength < 0) throw new InvalidDataException("长度异常。");

        Span<byte> buf4 = stackalloc byte[4];
        fs.ReadExactly(buf4);
        int chunkSize = BitConverter.ToInt32(buf4);
        fs.ReadExactly(buf4);
        int chunks = BitConverter.ToInt32(buf4);
        if (chunkSize <= 0 || chunks <= 0) throw new InvalidDataException("分块参数异常。");

        // hint
        Span<byte> hintLenBuf = stackalloc byte[2];
        fs.ReadExactly(hintLenBuf);
        ushort hintLen = BitConverter.ToUInt16(hintLenBuf);

        long hintBytesOffset = fs.Position;
        long hintBytesCount = hintUtf16 ? (long)hintLen * 2 : hintLen;
        long hintEnd = hintBytesOffset + hintBytesCount;
        if (hintEnd > fs.Length) throw new InvalidDataException("文件损坏。");
        fs.Position = hintEnd;

        long tagTableOffset = fs.Position;
        long cipherOffset = tagTableOffset + (long)chunks * 16;
        if (cipherOffset > fs.Length) throw new InvalidDataException("文件损坏。");

        // Read tags
        byte[] tags = new byte[chunks * 16];
        fs.ReadExactly(tags);

        var key = DeriveKey(password, salt);
        using var aes = new AesGcm(key, 16);

        using var output = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.Read);
        fs.Position = cipherOffset;

        byte[] cipherBuf = new byte[chunkSize];
        byte[] plainBuf = new byte[chunkSize];
        long remainingPlain = originalLength;

        for (int i = 0; i < chunks && remainingPlain > 0; i++)
        {
            int toRead = (int)Math.Min(chunkSize, remainingPlain);
            fs.ReadExactly(cipherBuf.AsSpan(0, toRead));

            Span<byte> nonce = stackalloc byte[12];
            DeriveChunkNonce(baseNonce, i, nonce);

            ReadOnlySpan<byte> tag = tags.AsSpan(i * 16, 16);
            aes.Decrypt(nonce, cipherBuf.AsSpan(0, toRead), tag, plainBuf.AsSpan(0, toRead));
            output.Write(plainBuf, 0, toRead);
            remainingPlain -= toRead;
        }

        CryptographicOperations.ZeroMemory(plainBuf);
        CryptographicOperations.ZeroMemory(cipherBuf);
        CryptographicOperations.ZeroMemory(tags);
        CryptographicOperations.ZeroMemory(key);
    }

    // hint reading happens via TryReadHintFromFile on crypto failure

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        // PBKDF2 for now to avoid extra deps; can upgrade to Argon2id later.
        using var kdf = new Rfc2898DeriveBytes(password, salt, 200_000, HashAlgorithmName.SHA256);
        return kdf.GetBytes(32);
    }

    private static List<string> ExpandInputs(string input, bool recursive)
    {
        var list = new List<string>();

        // directory
        if (Directory.Exists(input))
        {
            var opt = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true };
            foreach (var f in Directory.EnumerateFiles(input, "*.*", opt))
            {
                if (IsVideoFile(f)) list.Add(f);
            }
            return list;
        }

        // wildcard
        if (input.Contains('*') || input.Contains('?'))
        {
            var dir = Path.GetDirectoryName(input);
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            var pattern = Path.GetFileName(input);
            var opt = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true };
            foreach (var f in Directory.EnumerateFiles(dir!, pattern, opt))
            {
                if (IsVideoFile(f)) list.Add(f);
            }
            return list;
        }

        // file
        if (File.Exists(input))
            list.Add(input);

        return list;
    }

    private static bool IsVideoFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".webm", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveOutputPath(string inputFile, string? outArg)
    {
        var inputFull = Path.GetFullPath(inputFile);

        // If --out is a directory, preserve filename.
        if (!string.IsNullOrWhiteSpace(outArg))
        {
            var outPath = outArg.Trim('"');

            if (outPath.EndsWith(Path.DirectorySeparatorChar) || outPath.EndsWith(Path.AltDirectorySeparatorChar) || Directory.Exists(outPath))
            {
                var dir = outPath;
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, Path.GetFileNameWithoutExtension(inputFull) + ".llv");
            }

            // If --out is a file path, use it (single input recommended).
            if (outPath.EndsWith(".llv", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(outPath);

            // otherwise treat as directory
            Directory.CreateDirectory(outPath);
            return Path.Combine(outPath, Path.GetFileNameWithoutExtension(inputFull) + ".llv");
        }

        // default: alongside original
        var dirDefault = Path.GetDirectoryName(inputFull)!;
        return Path.Combine(dirDefault, Path.GetFileNameWithoutExtension(inputFull) + ".llv");
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) sb.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar))
                sb.Append(key.KeyChar);
        }
        return sb.ToString();
    }

    private static void PrintEncryptHelp()
    {
        UI.PrintHeader("encv - 加密视频文件");
        UI.PrintInfo("用法: encv <file|dir|glob> [--out=输出路径] [--pwd=密码] [-r] [--chunk=字节] [--hint] [--hint=提示]");
        Console.WriteLine();
        UI.PrintResult("encv a.mp4", "加密到同目录 a.llv");
        UI.PrintResult("encv D:/videos --out=E:/vault -r", "递归批量加密到 E:/vault");
        UI.PrintResult("encv \"D:/videos/*.mp4\" --out=E:/vault", "按通配符批量加密");
        UI.PrintResult("encv a.mp4 --chunk=1048576", "设置分块大小(默认 1MB)，便于大文件处理");
        UI.PrintResult("encv a.mp4 --hint=手机号后四位", "写入密码提示(最多 200 字节)，仅在密码错误时显示");
        UI.PrintInfo("提示字段使用 UTF-16 存储以避免中文乱码(最多 100 字)。");
        UI.PrintResult("encv D:/videos -r --hint", "批量加密时交互输入一次提示(可空)");
        UI.PrintInfo("提示: 不写 --pwd 会交互输入密码。");
    }

    private static void PrintPlayHelp()
    {
        UI.PrintHeader("playv - 播放加密视频");
        UI.PrintInfo("用法: playv <file.llv|dir> [--pwd=密码] [--autodel] [--html5]");
        Console.WriteLine();
        UI.PrintResult("playv a.llv", "输入密码后播放");
        UI.PrintResult("playv E:/vault", "从目录中选择要播放的 .llv");
        UI.PrintResult("playv E:/vault --autodel", "播放后自动尝试删除临时文件(播放器若仍占用会延迟)");
        UI.PrintResult("playv a.llv --html5", "使用浏览器 HTML5 播放器播放，支持拖拽进度");
        UI.PrintInfo("默认使用系统播放器；--html5 选项使用本地 HTTP 服务提供 HTML5 播放器。清理请用 clrv。");
    }

    private static void PrintListHelp()
    {
        UI.PrintHeader("lsv - 列出加密视频");
        UI.PrintInfo("用法: lsv [dir] [-r]");
    }

    private static IEnumerable<string> EnumerateLlvFiles(string root, bool recursive)
    {
        var opt = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(root, "*.llv", opt)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveLlvFileFromArgument(string target)
    {
        if (File.Exists(target))
            return target;

        if (!Directory.Exists(target))
        {
            UI.PrintError($"文件/目录不存在: {target}");
            return null;
        }

        var files = EnumerateLlvFiles(target, recursive: false).ToArray();
        if (files.Length == 0)
        {
            UI.PrintError("目录中没有 .llv 文件。");
            return null;
        }

        UI.PrintHeader("选择要播放的视频");
        for (int i = 0; i < files.Length; i++)
        {
            Console.WriteLine($"[{i + 1,3}] {Path.GetFileName(files[i])}");
        }
        Console.Write("请输入序号: ");
        var s = Console.ReadLine();
        if (!int.TryParse(s, out var idx) || idx < 1 || idx > files.Length)
        {
            UI.PrintError("输入无效。");
            return null;
        }

        return files[idx - 1];
    }

    private static int ReadAtMost(Stream s, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = s.Read(buffer, total, buffer.Length - total);
            if (n <= 0) break;
            total += n;
            if (!s.CanSeek) break;
            if (n == 0) break;
            // for files, one read usually enough; keep loop for completeness
        }
        return total;
    }

    private static void DeriveChunkNonce(ReadOnlySpan<byte> baseNonce, int chunkIndex, Span<byte> nonce)
    {
        baseNonce.CopyTo(nonce);
        // XOR last 4 bytes with chunk index (little-endian)
        nonce[^4] ^= (byte)(chunkIndex);
        nonce[^3] ^= (byte)(chunkIndex >> 8);
        nonce[^2] ^= (byte)(chunkIndex >> 16);
        nonce[^1] ^= (byte)(chunkIndex >> 24);
    }

    private static int GetFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint).Port;
    }

    private static void RunVideoServer(string tempFile, int port)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        // Run server for a reasonable time (e.g., 1 hour) or until error
        var cts = new CancellationTokenSource(TimeSpan.FromHours(1));
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var context = listener.GetContext();
                if (context.Request.Url.AbsolutePath == "/")
                {
                    ServeHtml(context.Response);
                }
                else if (context.Request.Url.AbsolutePath == "/video")
                {
                    ServeVideo(context, tempFile);
                }
                else
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                }
            }
        }
        catch (HttpListenerException)
        {
            // Server stopped
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void ServeHtml(HttpListenerResponse response)
    {
        string html = @"<!DOCTYPE html>
<html>
<head>
<title>LL Video Player</title>
<style>
body { margin: 0; padding: 0; background: black; overflow: hidden; }
video { width: 100vw; height: 100vh; object-fit: contain; }
</style>
</head>
<body>
<video controls autoplay muted>
<source src='/video' type='video/mp4'>
Your browser does not support the video tag.
</video>
</body>
</html>";
        byte[] buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    private static void ServeVideo(HttpListenerContext context, string tempFile)
    {
        var response = context.Response;
        var request = context.Request;
        response.ContentType = "video/mp4";
        response.Headers.Add("Accept-Ranges", "bytes");

        var fi = new FileInfo(tempFile);
        long totalSize = fi.Length;

        using var fs = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read);

        long start = 0;
        long end = totalSize - 1;
        bool isPartial = false;

        if (request.Headers["Range"] != null)
        {
            var range = ParseRange(request.Headers["Range"], totalSize);
            start = range.Start;
            end = range.End;
            isPartial = true;
            response.StatusCode = 206;
            response.Headers.Add("Content-Range", $"bytes {start}-{end}/{totalSize}");
        }

        long contentLength = end - start + 1;
        response.ContentLength64 = contentLength;

        fs.Position = start;

        byte[] buffer = new byte[8192];
        long bytesToSend = contentLength;
        try
        {
            while (bytesToSend > 0)
            {
                int toRead = (int)Math.Min(buffer.Length, bytesToSend);
                int read = fs.Read(buffer, 0, toRead);
                if (read == 0) break;
                response.OutputStream.Write(buffer, 0, read);
                bytesToSend -= read;
            }
        }
        catch (HttpListenerException)
        {
            // Client disconnected, ignore
        }
        finally
        {
            response.OutputStream.Close();
        }
    }

    private static (long Start, long End) ParseRange(string rangeHeader, long totalSize)
    {
        var parts = rangeHeader.Split('=');
        if (parts.Length != 2 || parts[0] != "bytes") return (0, totalSize - 1);
        var ranges = parts[1].Split('-');
        if (ranges.Length != 2) return (0, totalSize - 1);
        long start = long.Parse(ranges[0]);
        long end = string.IsNullOrEmpty(ranges[1]) ? totalSize - 1 : long.Parse(ranges[1]);
        return (start, Math.Min(end, totalSize - 1));
    }
}
