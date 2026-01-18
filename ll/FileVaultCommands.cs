using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Sockets;

namespace LL;

public static class FileVaultCommands
{
    private static readonly byte[] MagicF2 = Encoding.ASCII.GetBytes("LLF2");

    private const int DefaultChunkSize = 1024 * 1024; // 1MB
    private const int MaxHintBytes = 200;
    private const ushort FlagHintUtf16 = 1;

    public static void EncryptFile(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            PrintEncryptFileHelp();
            return;
        }

        string input = args[0];
        string? outPath = null;
        string? password = null;
        bool recursive = false;
        string? hint = null;
        bool askHint = false;

        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--out=", StringComparison.OrdinalIgnoreCase)) outPath = a["--out=".Length..].Trim('"');
            else if (a.StartsWith("--pwd=", StringComparison.OrdinalIgnoreCase)) password = a["--pwd=".Length..];
            else if (a is "-r" or "--recursive") recursive = true;
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

        if (string.IsNullOrWhiteSpace(hint) && askHint)
        {
            Console.Write("密码提示(可空, 最多 100 字): ");
            hint = Console.ReadLine();
        }

        var inputs = ExpandFileInputs(input, recursive);
        if (inputs.Count == 0)
        {
            UI.PrintError($"未找到文件: {input}");
            return;
        }

        UI.PrintHeader("文件加密");
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
                var dst = ResolveEncryptFileOutputPath(file, outPath);
                var fi = new FileInfo(file);
                EncryptFileOne(file, dst, password, hint);
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

    private static void EncryptFileOne(string inputFile, string outputFile, string password, string? hint)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);

        var salt = RandomNumberGenerator.GetBytes(16);
        var baseNonce = RandomNumberGenerator.GetBytes(12);
        var key = DeriveKey(password, salt);

        var info = new FileInfo(inputFile);
        long originalLength = info.Length;
        var originalFileName = Path.GetFileName(inputFile);

        using var input = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None);

        // Header: "LLF2" + flags + salt + baseNonce + originalLength + chunkSize + chunks + originalFileName + hint
        output.Write(Encoding.ASCII.GetBytes("LLF2"));
        byte flags = 0;
        flags |= (byte)FlagHintUtf16;
        output.WriteByte(flags);
        output.Write(salt);
        output.Write(baseNonce);
        output.Write(BitConverter.GetBytes(originalLength));
        output.Write(BitConverter.GetBytes(DefaultChunkSize));
        int chunks = (int)((originalLength + DefaultChunkSize - 1) / DefaultChunkSize);
        output.Write(BitConverter.GetBytes(chunks));

        // Original file name (UTF-16LE)
        var originalFileNameBytes = Encoding.Unicode.GetBytes(originalFileName);
        output.Write(BitConverter.GetBytes((ushort)originalFileNameBytes.Length));
        output.Write(originalFileNameBytes, 0, originalFileNameBytes.Length);

        // Hint
        ushort hintCharCount = 0;
        byte[] hintBytes = Array.Empty<byte>();
        if (!string.IsNullOrWhiteSpace(hint))
        {
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

        // Reserve tag table
        long tagTableOffset = output.Position;
        output.Position += (long)chunks * 16;

        var tags = new byte[chunks * 16];
        byte[] plainBuf = new byte[DefaultChunkSize];
        byte[] cipherBuf = new byte[DefaultChunkSize];

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

    public static void DecryptFile(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            PrintDecryptFileHelp();
            return;
        }

        string input = args[0];
        string? outPath = null;
        string? password = null;
        bool recursive = false;

        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--out=", StringComparison.OrdinalIgnoreCase)) outPath = a["--out=".Length..].Trim('"');
            else if (a.StartsWith("--pwd=", StringComparison.OrdinalIgnoreCase)) password = a["--pwd=".Length..];
            else if (a is "-r" or "--recursive") recursive = true;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            password = ReadPassword("请输入密码: ");
        }

        var inputs = ExpandLlfInputs(input, recursive);
        if (inputs.Count == 0)
        {
            UI.PrintError($"未找到 .llf 文件: {input}");
            return;
        }

        UI.PrintHeader("文件解密");
        UI.PrintResult("文件数", inputs.Count.ToString("n0"));
        long totalBytes = 0;
        foreach (var f in inputs)
        {
            try { totalBytes += new FileInfo(f).Length; } catch { }
        }
        UI.PrintResult("总大小", Utils.FormatSize(totalBytes));
        UI.PrintResult("输出", string.IsNullOrWhiteSpace(outPath) ? "同目录" : outPath);
        UI.PrintInfo("开始解密...");
        Console.WriteLine();

        var swAll = Stopwatch.StartNew();
        int ok = 0;
        int fail = 0;
        long doneBytes = 0;

        foreach (var file in inputs)
        {
            try
            {
                var dst = ResolveDecryptFileOutputPath(file, outPath);
                var fi = new FileInfo(file);
                DecryptFileOne(file, dst, password);
                ok++;
                doneBytes += fi.Length;

                var elapsed = swAll.Elapsed;
                var speed = elapsed.TotalSeconds > 0.1 ? doneBytes / elapsed.TotalSeconds : 0;
                var remainBytes = Math.Max(0, totalBytes - doneBytes);
                var eta = speed > 1 ? TimeSpan.FromSeconds(remainBytes / speed) : TimeSpan.Zero;
                UI.PrintResult("进度", $"{ok + fail}/{inputs.Count}  已完成 {Utils.FormatSize(doneBytes)} / {Utils.FormatSize(totalBytes)}  预计剩余 {eta:hh\\:mm\\:ss}");
                UI.PrintSuccess($"已解密: {file} -> {dst}");
            }
            catch (CryptographicException)
            {
                // Show hint only on password/crypto failure.
                var hint = TryReadHintFromFileForLLF(file);
                if (!string.IsNullOrWhiteSpace(hint))
                {
                    UI.PrintInfo("密码提示:");
                    PrintTextSafe(hint);
                }
                UI.PrintError("密码错误或文件已损坏。");
                fail++;
            }
            catch (Exception ex)
            {
                fail++;
                UI.PrintError($"解密失败: {file} ({ex.Message})");
            }
        }

        swAll.Stop();
        Console.WriteLine();
        UI.PrintSuccess($"完成: 成功 {ok:n0}, 失败 {fail:n0}, 用时 {swAll.Elapsed:hh\\:mm\\:ss}");
    }

    private static void DecryptFileOne(string inputFile, string outputFile, string password)
    {
        using var fs = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[4];
        if (fs.Read(header) != 4)
            throw new InvalidDataException("文件头损坏。");

        if (!header.SequenceEqual(MagicF2))
            throw new InvalidDataException("不是有效的 .llf 文件。");

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

        // Original file name
        Span<byte> originalFileNameLenBuf = stackalloc byte[2];
        fs.ReadExactly(originalFileNameLenBuf);
        ushort originalFileNameLen = BitConverter.ToUInt16(originalFileNameLenBuf);
        byte[] originalFileNameBytes = new byte[originalFileNameLen];
        fs.ReadExactly(originalFileNameBytes);
        string originalFileName = Encoding.Unicode.GetString(originalFileNameBytes);

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

        // Use original file name for output if not specified
        string actualOutputFile = outputFile;
        if (string.IsNullOrEmpty(Path.GetExtension(actualOutputFile)))
        {
            var dir = Path.GetDirectoryName(actualOutputFile)!;
            actualOutputFile = Path.Combine(dir, originalFileName);
        }

        using var output = new FileStream(actualOutputFile, FileMode.Create, FileAccess.Write, FileShare.Read);
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

    private static string ResolveEncryptFileOutputPath(string inputFile, string? outArg)
    {
        var inputFull = Path.GetFullPath(inputFile);

        if (!string.IsNullOrWhiteSpace(outArg))
        {
            var outPath = outArg.Trim('"');

            if (outPath.EndsWith(Path.DirectorySeparatorChar) || outPath.EndsWith(Path.AltDirectorySeparatorChar) || Directory.Exists(outPath))
            {
                var dir = outPath;
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, Path.GetFileNameWithoutExtension(inputFull) + ".llf");
            }

            if (outPath.EndsWith(".llf", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(outPath);

            Directory.CreateDirectory(outPath);
            return Path.Combine(outPath, Path.GetFileNameWithoutExtension(inputFull) + ".llf");
        }

        var dirDefault = Path.GetDirectoryName(inputFull)!;
        return Path.Combine(dirDefault, Path.GetFileNameWithoutExtension(inputFull) + ".llf");
    }

    private static string ResolveDecryptFileOutputPath(string inputFile, string? outArg)
    {
        var inputFull = Path.GetFullPath(inputFile);

        if (!string.IsNullOrWhiteSpace(outArg))
        {
            var outPath = outArg.Trim('"');

            if (outPath.EndsWith(Path.DirectorySeparatorChar) || outPath.EndsWith(Path.AltDirectorySeparatorChar) || Directory.Exists(outPath))
            {
                var dir = outPath;
                Directory.CreateDirectory(dir);
                // Will be overridden by original file name in DecryptFileOne
                return Path.Combine(dir, "temp");
            }

            return Path.GetFullPath(outPath);
        }

        var dirDefault = Path.GetDirectoryName(inputFull)!;
        // Will be overridden by original file name in DecryptFileOne
        return Path.Combine(dirDefault, "temp");
    }

    private static void PrintEncryptFileHelp()
    {
        UI.PrintHeader("encf - 加密文件");
        UI.PrintInfo("用法: encf <file|dir|glob> [--out=输出路径] [--pwd=密码] [-r] [--hint] [--hint=提示]");
        Console.WriteLine();
        UI.PrintResult("encf secret.txt", "加密到同目录 secret.llf");
        UI.PrintResult("encf D:/files --out=E:/vault -r", "递归批量加密到 E:/vault");
        UI.PrintResult("encf \"D:/files/*.txt\" --out=E:/vault", "按通配符批量加密");
        UI.PrintResult("encf secret.txt --hint=生日", "写入密码提示");
        UI.PrintInfo("提示: 使用 AES-GCM 加密，安全可靠。");
    }

    private static void PrintDecryptFileHelp()
    {
        UI.PrintHeader("decf - 解密文件");
        UI.PrintInfo("用法: decf <file.llf|dir|glob> [--out=输出路径] [--pwd=密码] [-r]");
        Console.WriteLine();
        UI.PrintResult("decf secret.llf", "解密到同目录 secret.txt");
        UI.PrintResult("decf D:/vault --out=E:/files -r", "递归批量解密到 E:/files");
        UI.PrintResult("decf \"D:/vault/*.llf\" --out=E:/files", "按通配符批量解密");
        UI.PrintInfo("提示: 密码错误时显示提示（如果有）。");
    }

    private static string? TryReadHintFromFileForLLF(string file)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> magic = stackalloc byte[4];
            if (fs.Read(magic) != 4) return null;

            // Only LLF2 has hint.
            if (!magic.SequenceEqual(MagicF2)) return null;

            int flagsByte = fs.ReadByte();
            if (flagsByte < 0) return null;
            bool hintUtf16 = (flagsByte & FlagHintUtf16) != 0;

            fs.Position += 16; // salt
            fs.Position += 12; // baseNonce
            fs.Position += 8;  // originalLength
            fs.Position += 4;  // chunkSize
            fs.Position += 4;  // chunks

            // Skip original file name
            Span<byte> originalFileNameLenBuf = stackalloc byte[2];
            fs.ReadExactly(originalFileNameLenBuf);
            ushort originalFileNameLen = BitConverter.ToUInt16(originalFileNameLenBuf);
            fs.Position += originalFileNameLen;

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

    // Shared methods from VideoVaultCommands
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

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        // PBKDF2 for now to avoid extra deps; can upgrade to Argon2id later.
        using var kdf = new Rfc2898DeriveBytes(password, salt, 200_000, HashAlgorithmName.SHA256);
        return kdf.GetBytes(32);
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
    
    private static void ReadExactly(this Stream s, byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = s.Read(buffer, read, buffer.Length - read);
            if (n <= 0) throw new EndOfStreamException();
            read += n;
        }
    }

    private static List<string> ExpandFileInputs(string input, bool recursive)
    {
        var list = new List<string>();

        // directory
        if (Directory.Exists(input))
        {
            var opt = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true };
            foreach (var f in Directory.EnumerateFiles(input, "*.*", opt))
            {
                list.Add(f);
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
                list.Add(f);
            }
            return list;
        }

        // file
        if (File.Exists(input))
            list.Add(input);

        return list;
    }

    private static List<string> ExpandLlfInputs(string input, bool recursive)
    {
        var list = new List<string>();

        // directory
        if (Directory.Exists(input))
        {
            var opt = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true };
            foreach (var f in Directory.EnumerateFiles(input, "*.*", opt))
            {
                if (Path.GetExtension(f).Equals(".llf", StringComparison.OrdinalIgnoreCase))
                    list.Add(f);
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
                if (Path.GetExtension(f).Equals(".llf", StringComparison.OrdinalIgnoreCase))
                    list.Add(f);
            }
            return list;
        }

        // file
        if (File.Exists(input) && Path.GetExtension(input).Equals(".llf", StringComparison.OrdinalIgnoreCase))
            list.Add(input);

        return list;
    }
}