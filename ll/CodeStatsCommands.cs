using System.Collections.Concurrent;
using System.Diagnostics;

namespace LL;

public static class CodeStatsCommands
{
    private const int LangColWidth = 18;

    // Column start positions (0-based) for fixed-layout tables.
    private const int ColLang = 0;
    private const int ColFiles = 22;
    private const int ColCode = 33;
    private const int ColComment = 45;
    private const int ColBlank = 57;
    private const int ColTotal = 69;
    private const int ColSize = 81;
    private sealed record Language(string Name, string[] Extensions);

    private static readonly Language[] Languages =
    [
        new("C#", [".cs", ".csx", ".razor", ".xaml"]),
        new("F#", [".fs", ".fsx"]),
        new("VB", [".vb"]),
        new(".NET Project", [".csproj", ".fsproj", ".vbproj", ".props", ".targets", ".config", ".nuspec", ".sln"]),

        new("Java", [".java"]),
        new("Kotlin", [".kt", ".kts"]),
        new("Scala", [".scala"]),
        new("Gradle/Groovy", [".gradle", ".groovy"]),

        new("TypeScript", [".ts", ".mts", ".cts", ".tsx"]),
        new("JavaScript", [".js", ".mjs", ".cjs", ".jsx"]),

        new("C", [".c", ".h"]),
        new("C++", [".cc", ".cpp", ".cxx", ".c++", ".hh", ".hpp", ".hxx", ".inl", ".i", ".ii"]),

        new("Go", [".go"]),
        new("Python", [".py", ".pyw"]),
        new("Rust", [".rs"]),
        new("PHP", [".php", ".phtml"]),
        new("Ruby", [".rb"]),

        new("Shell", [".sh", ".bash", ".zsh", ".cmd", ".bat"]),
        new("PowerShell", [".ps1", ".psm1", ".psd1"]),

        new("Web", [".html", ".htm", ".css", ".scss", ".sass", ".less"]),
        new("Config/Data", [".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".editorconfig", ".dockerfile", ".cmake", ".make", ".mk"]),
        new("Docs", [".md", ".rst", ".txt"])
    ];

    private static readonly Dictionary<string, string> ExtensionToLanguage = BuildExtensionToLanguageMap();

    private sealed record Options(
        string Root,
        bool FollowSymlinks,
        int RefreshMs,
        int MaxDegree,
        string[] Extensions,
        bool IncludeHidden,
        bool IncludeBinObj);

    private sealed class Stats
    {
        public long Files;
        public long TotalLines;
        public long BlankLines;
        public long CommentLines;
        public long CodeLines;
        public long Bytes;

        public ConcurrentDictionary<string, LangStats> ByLanguage { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Add(in FileStats s)
        {
            Interlocked.Increment(ref Files);
            Interlocked.Add(ref TotalLines, s.TotalLines);
            Interlocked.Add(ref BlankLines, s.BlankLines);
            Interlocked.Add(ref CommentLines, s.CommentLines);
            Interlocked.Add(ref CodeLines, s.CodeLines);
            Interlocked.Add(ref Bytes, s.Bytes);

            var lang = string.IsNullOrWhiteSpace(s.Language) ? "Other" : s.Language;
            var bucket = ByLanguage.GetOrAdd(lang, static _ => new LangStats());
            bucket.Add(s);
        }
    }

    private sealed class LangStats
    {
        public long Files;
        public long TotalLines;
        public long BlankLines;
        public long CommentLines;
        public long CodeLines;
        public long Bytes;

        public void Add(in FileStats s)
        {
            Interlocked.Increment(ref Files);
            Interlocked.Add(ref TotalLines, s.TotalLines);
            Interlocked.Add(ref BlankLines, s.BlankLines);
            Interlocked.Add(ref CommentLines, s.CommentLines);
            Interlocked.Add(ref CodeLines, s.CodeLines);
            Interlocked.Add(ref Bytes, s.Bytes);
        }
    }

    private readonly record struct FileStats(long TotalLines, long BlankLines, long CommentLines, long CodeLines, long Bytes, string Language);

    public static void Run(string[] args)
    {
        if (args.Length > 0 && (args[0] is "help" or "?" or "-h" or "--help"))
        {
            PrintHelp();
            return;
        }

        var opt = ParseOptions(args);

        if (!Directory.Exists(opt.Root))
        {
            UI.PrintError($"目录不存在: {opt.Root}");
            return;
        }

        UI.PrintHeader("代码统计 (LOC)");
        UI.PrintResult("目录", Path.GetFullPath(opt.Root));
        UI.PrintResult("扩展名", string.Join(", ", opt.Extensions));
        UI.PrintResult("刷新间隔", $"{opt.RefreshMs} ms");
        UI.PrintResult("并行度", opt.MaxDegree.ToString());
        UI.PrintInfo("扫描中... (按 Ctrl+C 终止)");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += handler;

        try
        {
            RunInternal(opt, cts.Token);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private static void RunInternal(Options opt, CancellationToken token)
    {
        var stats = new Stats();
        var sw = Stopwatch.StartNew();

        var startTop = Console.CursorTop;
        const int reservedLines = 30;
        ReserveScreenArea(reservedLines);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(opt.RefreshMs));

        var fileQueue = new BlockingCollection<string>(boundedCapacity: 8192);
        var producer = Task.Run(() =>
        {
            try
            {
                foreach (var file in EnumerateCandidateFiles(opt))
                {
                    token.ThrowIfCancellationRequested();
                    fileQueue.Add(file, token);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                fileQueue.CompleteAdding();
            }
        }, token);

        var consumers = Enumerable.Range(0, opt.MaxDegree).Select(_ => Task.Run(() =>
        {
            foreach (var file in fileQueue.GetConsumingEnumerable(token))
            {
                try
                {
                    var s = AnalyzeFile(file);
                    stats.Add(s);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Keep scanning; ignore single-file failures (locked/encoding etc.)
                }
            }
        }, token)).ToArray();

        bool completed = false;
        while (!completed && !token.IsCancellationRequested)
        {
            WriteLive(stats, sw, startTop, completed: false, reservedLines);

            var tick = timer.WaitForNextTickAsync(token);
            var all = Task.WhenAll(consumers.Append(producer));
            var done = Task.WhenAny(tick.AsTask(), all);
            try { done.GetAwaiter().GetResult(); } catch { }

            completed = Task.WhenAll(consumers.Append(producer)).IsCompleted;
        }

        try { Task.WhenAll(consumers.Append(producer)).GetAwaiter().GetResult(); } catch { }
        WriteLive(stats, sw, startTop, completed: true, reservedLines);
        // Print completion message right after the live dashboard (no extra blank area).
        Console.SetCursorPosition(0, startTop + reservedLines - 1);
        Console.Write(new string(' ', Math.Max(0, Console.BufferWidth - 1)));
        Console.SetCursorPosition(0, startTop + reservedLines - 1);
        UI.PrintSuccess("完成");
        Console.SetCursorPosition(0, startTop + reservedLines);
    }

    private static void WriteLive(Stats stats, Stopwatch sw, int startTop, bool completed, int reservedLines)
    {
        ClearRegion(startTop, reservedLines);

        var files = Interlocked.Read(ref stats.Files);
        var total = Interlocked.Read(ref stats.TotalLines);
        var blank = Interlocked.Read(ref stats.BlankLines);
        var comment = Interlocked.Read(ref stats.CommentLines);
        var code = Interlocked.Read(ref stats.CodeLines);
        var bytes = Interlocked.Read(ref stats.Bytes);

        var elapsed = sw.Elapsed;
        double linesPerSec = elapsed.TotalSeconds > 0.001 ? total / elapsed.TotalSeconds : 0;
        double commentRate = total > 0 ? (double)comment / total : 0;
        double blankRate = total > 0 ? (double)blank / total : 0;
        double codeRate = total > 0 ? (double)code / total : 0;
        double avgLinesPerFile = files > 0 ? (double)total / files : 0;
        var fileStats = ComputeFileLineStats();

        Console.SetCursorPosition(0, startTop);
        Console.WriteLine($"状态     : {(completed ? "完成" : "扫描中")}   用时: {elapsed:hh\\:mm\\:ss}   速度: {linesPerSec:0} 行/秒");

        Console.WriteLine($"文件数   : {files:n0}");

        Console.WriteLine($"总行数   : {total:n0}");

        Console.WriteLine($"代码行   : {code:n0}");

        Console.WriteLine($"注释行   : {comment:n0}");

        Console.WriteLine($"空白行   : {blank:n0}");

        Console.WriteLine($"总大小   : {Utils.FormatSize(bytes)}");

        Console.WriteLine($"占比     : 代码 {codeRate:P1}  注释 {commentRate:P1}  空白 {blankRate:P1}  密度 {(1 - blankRate):P1}");
        Console.WriteLine($"文件规模 : 平均 {avgLinesPerFile:0.0} 行/文件；一半文件不超过 {fileStats.P50:0} 行；90% 文件不超过 {fileStats.P90:0} 行");
        Console.WriteLine($"          最大文件 {fileStats.Max:n0} 行；大文件数量(>500 行) {fileStats.Gt500:n0}，超大文件(>1000 行) {fileStats.Gt1000:n0}");

        Console.WriteLine();
        Console.WriteLine("按语言分组 (按代码行 Top 8):");

        var snapshot = stats.ByLanguage
            .Select(kv => (Lang: kv.Key, Stats: kv.Value,
                Files: Interlocked.Read(ref kv.Value.Files),
                Code: Interlocked.Read(ref kv.Value.CodeLines),
                Comment: Interlocked.Read(ref kv.Value.CommentLines),
                Blank: Interlocked.Read(ref kv.Value.BlankLines),
                Total: Interlocked.Read(ref kv.Value.TotalLines),
                Bytes: Interlocked.Read(ref kv.Value.Bytes)))
            .Where(x => x.Total > 0)
            .OrderByDescending(x => x.Code)
            .ThenByDescending(x => x.Total)
            .Take(8)
            .ToArray();

        WriteLangHeader();
        foreach (var x in snapshot)
        {
            WriteLangRow(
                x.Lang,
                x.Files.ToString("n0"),
                x.Code.ToString("n0"),
                x.Comment.ToString("n0"),
                x.Blank.ToString("n0"),
                x.Total.ToString("n0"),
                Utils.FormatSize(x.Bytes));
        }

        // Overwrite remaining table rows if the previous frame had more rows.
        int padLang = Math.Max(0, 8 - snapshot.Length);
        for (int i = 0; i < padLang; i++)
            WriteLangRow(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        // Top files by code lines (best-effort; may be slightly stale due to concurrent updates)
        Console.WriteLine();
        Console.WriteLine("最大文件 (按代码行 Top 5):");
        var topFiles = GetTopFilesByCodeLines(stats, 5);
        WriteTopFileHeader();
        foreach (var f in topFiles)
        {
            WriteTopFileRow(
                f.CodeLines.ToString("n0"),
                f.TotalLines.ToString("n0"),
                Utils.FormatSize(f.Bytes),
                f.Path);
        }

        int padFiles = Math.Max(0, 5 - topFiles.Length);
        for (int i = 0; i < padFiles; i++)
            WriteTopFileRow(string.Empty, string.Empty, string.Empty, string.Empty);
    }

    private static void ReserveScreenArea(int lines)
    {
        // Pre-allocate a fixed area so the live report never scrolls the console.
        for (int i = 0; i < lines; i++)
            Console.WriteLine();
    }

    private static void ClearRegion(int startTop, int lines)
    {
        int width = Console.BufferWidth;
        for (int i = 0; i < lines; i++)
        {
            Console.SetCursorPosition(0, startTop + i);
            Console.Write(new string(' ', Math.Max(0, width - 1)));
        }
        Console.SetCursorPosition(0, startTop);
    }

    private sealed record TopFile(string Path, long CodeLines, long TotalLines, long Bytes);

    private static readonly ConcurrentDictionary<string, (long CodeLines, long TotalLines, long Bytes)> FileMetrics = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct FileLineStats(double P50, double P90, long Max, long Gt500, long Gt1000);

    private static FileLineStats ComputeFileLineStats()
    {
        // Snapshot totals; keep it fast (used for live updates).
        var totals = FileMetrics.Values.Select(v => v.TotalLines).ToArray();
        if (totals.Length == 0) return new FileLineStats(0, 0, 0, 0, 0);

        Array.Sort(totals);
        static double Percentile(long[] sorted, double p)
        {
            if (sorted.Length == 0) return 0;
            if (p <= 0) return sorted[0];
            if (p >= 1) return sorted[^1];

            double pos = (sorted.Length - 1) * p;
            int i = (int)pos;
            double frac = pos - i;
            if (i + 1 >= sorted.Length) return sorted[i];
            return sorted[i] + (sorted[i + 1] - sorted[i]) * frac;
        }

        long max = totals[^1];
        long gt500 = totals.Count(x => x > 500);
        long gt1000 = totals.Count(x => x > 1000);
        return new FileLineStats(Percentile(totals, 0.5), Percentile(totals, 0.9), max, gt500, gt1000);
    }

    private static TopFile[] GetTopFilesByCodeLines(Stats stats, int take)
    {
        // Use the dedicated file snapshot map for stable listing.
        return FileMetrics
            .Select(kv => new TopFile(kv.Key, kv.Value.CodeLines, kv.Value.TotalLines, kv.Value.Bytes))
            .OrderByDescending(x => x.CodeLines)
            .ThenByDescending(x => x.TotalLines)
            .Take(take)
            .ToArray();
    }

    private static void ClearLine()
    {
        int width = Console.BufferWidth;
        Console.Write(new string(' ', Math.Max(0, width - 1)));
        Console.SetCursorPosition(0, Console.CursorTop);
    }

    private static void WriteAt(int left, string text)
    {
        var x = Math.Clamp(left, 0, Math.Max(0, Console.BufferWidth - 1));
        Console.SetCursorPosition(x, Console.CursorTop);
        Console.Write(text);
    }

    private static void WriteLangHeader()
    {
        Console.Write(new string(' ', Math.Max(0, Console.BufferWidth - 1)));
        Console.SetCursorPosition(0, Console.CursorTop);
        WriteAt(ColLang, "语言");
        WriteAt(ColFiles, "文件");
        WriteAt(ColCode, "代码行");
        WriteAt(ColComment, "注释行");
        WriteAt(ColBlank, "空白行");
        WriteAt(ColTotal, "总行数");
        WriteAt(ColSize, "大小");
        Console.WriteLine();
    }

    private static void WriteLangRow(string lang, string files, string code, string comment, string blank, string total, string size)
    {
        Console.Write(new string(' ', Math.Max(0, Console.BufferWidth - 1)));
        Console.SetCursorPosition(0, Console.CursorTop);
        WriteAt(ColLang, Truncate(lang, LangColWidth));
        WriteAt(ColFiles, files);
        WriteAt(ColCode, code);
        WriteAt(ColComment, comment);
        WriteAt(ColBlank, blank);
        WriteAt(ColTotal, total);
        WriteAt(ColSize, size);
        Console.WriteLine();
    }

    private static void WriteTopFileHeader()
    {
        Console.Write(new string(' ', Math.Max(0, Console.BufferWidth - 1)));
        Console.SetCursorPosition(0, Console.CursorTop);
        WriteAt(0, "代码行");
        WriteAt(12, "总行");
        WriteAt(24, "大小");
        WriteAt(36, "文件");
        Console.WriteLine();
    }

    private static void WriteTopFileRow(string code, string total, string size, string path)
    {
        Console.Write(new string(' ', Math.Max(0, Console.BufferWidth - 1)));
        Console.SetCursorPosition(0, Console.CursorTop);
        WriteAt(0, code);
        WriteAt(12, total);
        WriteAt(24, size);
        WriteAt(36, path);
        Console.WriteLine();
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length <= max) return s;
        if (max <= 1) return s[..1];
        return s[..(max - 1)] + "…";
    }

    private static IEnumerable<string> EnumerateCandidateFiles(Options opt)
    {
        var root = Path.GetFullPath(opt.Root);

        var enumOpt = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = opt.IncludeHidden ? 0 : FileAttributes.Hidden,
            ReturnSpecialDirectories = false
        };

        if (opt.FollowSymlinks)
            enumOpt.AttributesToSkip &= ~FileAttributes.ReparsePoint;
        else
            enumOpt.AttributesToSkip |= FileAttributes.ReparsePoint;

        var extSet = new HashSet<string>(opt.Extensions.Select(e => e.StartsWith('.') ? e : "." + e), StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(root, "*.*", enumOpt))
        {
            var ext = Path.GetExtension(file);
            if (!extSet.Contains(ext))
                continue;

            if (!opt.IncludeBinObj)
            {
                var pathNorm = file.Replace('\u005c', '/');
                if (pathNorm.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                    pathNorm.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                    pathNorm.Contains("/.git/", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            yield return file;
        }
    }

    private static FileStats AnalyzeFile(string file)
    {
        var lang = GuessLanguage(file);

        long total = 0;
        long blank = 0;
        long comment = 0;
        long code = 0;

        bool inBlockComment = false;
        foreach (var line in File.ReadLines(file))
        {
            total++;
            var s = line.AsSpan();
            if (IsBlank(s))
            {
                blank++;
                continue;
            }

            long cmtThisLine = 0;
            long codeThisLine = 0;
            AnalyzeLineCSharpLike(s, ref inBlockComment, out cmtThisLine, out codeThisLine);

            if (cmtThisLine > 0 && codeThisLine == 0) comment++;
            else if (cmtThisLine == 0 && codeThisLine > 0) code++;
            else if (cmtThisLine > 0 && codeThisLine > 0)
            {
                // mixed line: count as both for detail accuracy
                comment++;
                code++;
            }
            else
            {
                // fallback
                code++;
            }
        }

        var bytes = new FileInfo(file).Length;
        FileMetrics[file] = (code, total, bytes);
        return new FileStats(total, blank, comment, code, bytes, lang);
    }

    private static string GuessLanguage(string file)
    {
        var ext = Path.GetExtension(file);
        if (string.IsNullOrEmpty(ext)) return "Other";
        if (ExtensionToLanguage.TryGetValue(ext, out var lang)) return lang;
        return "Other";
    }

    private static Dictionary<string, string> BuildExtensionToLanguageMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lang in Languages)
        {
            foreach (var ext in lang.Extensions)
            {
                if (string.IsNullOrWhiteSpace(ext)) continue;
                var key = ext.StartsWith('.') ? ext : "." + ext;
                map.TryAdd(key, lang.Name);
            }
        }
        return map;
    }

    private static void AnalyzeLineCSharpLike(ReadOnlySpan<char> s, ref bool inBlockComment, out long commentPart, out long codePart)
    {
        // Heuristic lexer for // and /* */ comments, respecting string/char literals.
        commentPart = 0;
        codePart = 0;

        int i = 0;
        bool inString = false;
        bool inChar = false;
        bool verbatim = false;

        while (i < s.Length)
        {
            if (inBlockComment)
            {
                var end = s.Slice(i).IndexOf("*/", StringComparison.Ordinal);
                if (end >= 0)
                {
                    commentPart = 1;
                    i += end + 2;
                    inBlockComment = false;
                    continue;
                }

                commentPart = 1;
                return;
            }

            char ch = s[i];

            if (inString)
            {
                if (verbatim)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < s.Length && s[i + 1] == '"')
                        {
                            i += 2;
                            continue;
                        }
                        inString = false;
                        verbatim = false;
                    }
                    i++;
                    continue;
                }

                if (ch == '\\')
                {
                    i += Math.Min(2, s.Length - i);
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }
                i++;
                continue;
            }

            if (inChar)
            {
                if (ch == '\\')
                {
                    i += Math.Min(2, s.Length - i);
                    continue;
                }

                if (ch == '\'')
                {
                    inChar = false;
                }
                i++;
                continue;
            }

            // string / char start
            if (ch == '"')
            {
                inString = true;
                // detect verbatim @"..."
                if (i > 0 && s[i - 1] == '@')
                    verbatim = true;
                codePart = 1;
                i++;
                continue;
            }
            if (ch == '\'')
            {
                inChar = true;
                codePart = 1;
                i++;
                continue;
            }

            // comment start
            if (ch == '/' && i + 1 < s.Length)
            {
                char n = s[i + 1];
                if (n == '/')
                {
                    // treat rest as comment
                    commentPart = 1;
                    // consider code before //
                    if (HasNonWhitespace(s.Slice(0, i)))
                        codePart = 1;
                    return;
                }
                if (n == '*')
                {
                    commentPart = 1;
                    if (HasNonWhitespace(s.Slice(0, i)))
                        codePart = 1;
                    inBlockComment = true;
                    i += 2;
                    continue;
                }
            }

            if (!char.IsWhiteSpace(ch))
                codePart = 1;

            i++;
        }

        if (inBlockComment)
            commentPart = 1;
    }

    private static bool IsBlank(ReadOnlySpan<char> s)
    {
        foreach (var ch in s)
        {
            if (!char.IsWhiteSpace(ch)) return false;
        }
        return true;
    }

    private static bool HasNonWhitespace(ReadOnlySpan<char> s)
    {
        foreach (var ch in s)
        {
            if (!char.IsWhiteSpace(ch)) return true;
        }
        return false;
    }

    private static Options ParseOptions(string[] args)
    {
        string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();

        bool followSymlinks = false;
        int refreshMs = 200;
        int maxDegree = Math.Clamp(Environment.ProcessorCount, 2, 16);
        bool includeHidden = false;
        bool includeBinObj = false;

        string[] extensions =
        [
            // C# / .NET
            ".cs", ".csx", ".fs", ".fsx", ".vb",
            ".xaml", ".razor", ".csproj", ".fsproj", ".vbproj", ".sln",
            ".props", ".targets", ".config", ".nuspec",

            // Java / JVM
            ".java", ".kt", ".kts", ".groovy", ".gradle", ".scala",

            // JavaScript / TypeScript
            ".js", ".mjs", ".cjs", ".ts", ".mts", ".cts", ".jsx", ".tsx",

            // C / C++
            ".c", ".h", ".i", ".ii", ".cc", ".cpp", ".cxx", ".c++", ".hh", ".hpp", ".hxx", ".inl",

            // Go / Python / Rust
            ".go", ".py", ".pyw", ".rs",

            // PHP / Ruby
            ".php", ".phtml", ".rb",

            // Shell
            ".sh", ".bash", ".zsh", ".ps1", ".psm1", ".psd1", ".cmd", ".bat",

            // Web / markup / data / docs
            ".html", ".htm", ".css", ".scss", ".sass", ".less",
            ".json", ".xml", ".yaml", ".yml", ".toml", ".ini",
            ".md", ".rst", ".txt",

            // Build / CI
            ".cmake", ".make", ".mk", ".dockerfile", ".editorconfig"
        ];

        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--follow" or "-L") followSymlinks = true;
            else if (a is "--hidden") includeHidden = true;
            else if (a is "--binobj") includeBinObj = true;
            else if (a.StartsWith("--refresh=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a["--refresh=".Length..], out var r)) refreshMs = Math.Clamp(r, 50, 2000);
            else if (a.StartsWith("--j=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a["--j=".Length..], out var j)) maxDegree = Math.Clamp(j, 1, 64);
            else if (a.StartsWith("--ext=", StringComparison.OrdinalIgnoreCase))
            {
                var list = a["--ext=".Length..]
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.StartsWith('.') ? x : "." + x)
                    .ToArray();
                if (list.Length > 0) extensions = list;
            }
        }

        return new Options(root, followSymlinks, refreshMs, maxDegree, extensions, includeHidden, includeBinObj);
    }

    private static void PrintHelp()
    {
        UI.PrintHeader("loc - 目录代码统计");
        UI.PrintInfo("用法: loc [dir] [options]");
        Console.WriteLine();
        UI.PrintResult("loc", "统计当前目录");
        UI.PrintResult("loc D:/repo", "统计指定目录");
        UI.PrintInfo("默认会统计主流语言源码/工程文件(如 C#/Java/JS/TS/C/C++/Go/Python/Rust 等)。可用 --ext 覆盖。");
        UI.PrintResult("--ext=.cs,.js,.ts", "指定文件扩展名列表(逗号分隔)");
        UI.PrintResult("--refresh=200", "刷新间隔(ms), 默认 200");
        UI.PrintResult("--j=8", "并行度(线程数), 默认 CPU 核心数(2-16)");
        UI.PrintResult("--hidden", "包含隐藏文件");
        UI.PrintResult("--binobj", "包含 bin/obj/.git 等目录(默认排除)");
        UI.PrintResult("--follow | -L", "跟随符号链接(默认不跟随)");
        Console.WriteLine();
        UI.PrintInfo("说明: 注释/代码/空白为启发式统计(支持 // 和 /* */，会尽量忽略字符串中的注释符号)。");
    }
}
