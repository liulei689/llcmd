namespace LL;

public static class HistoryManager
{
    private const int MaxLines = 300;

    private static readonly List<string> _session = new();
    private static int _cursor = 0;

    public static void Add(string line)
    {
        line = (line ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(line)) return;

        try
        {
            var path = GetHistoryPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Avoid consecutive duplicates
            var last = TryReadLastLine(path);
            if (string.Equals(last, line, StringComparison.Ordinal))
                return;

            _session.Add(line);
            _cursor = _session.Count;

            File.AppendAllText(path, line + Environment.NewLine);
            TrimIfNeeded(path);
        }
        catch
        {
            // ignore history failures
        }
    }

    public static void EnsureSessionLoaded()
    {
        if (_session.Count > 0) return;
        try
        {
            var path = GetHistoryPath();
            if (!File.Exists(path)) return;
            _session.AddRange(File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(MaxLines));
            _cursor = _session.Count;
        }
        catch
        {
        }
    }

    public static string? Prev()
    {
        EnsureSessionLoaded();
        if (_session.Count == 0) return null;
        if (_cursor > 0) _cursor--;
        return _session[_cursor];
    }

    public static string? Next()
    {
        EnsureSessionLoaded();
        if (_session.Count == 0) return null;
        if (_cursor < _session.Count - 1)
        {
            _cursor++;
            return _session[_cursor];
        }
        _cursor = _session.Count;
        return string.Empty;
    }

    public static IReadOnlyList<string> ReadLast(int count)
    {
        count = Math.Clamp(count, 1, MaxLines);
        try
        {
            var path = GetHistoryPath();
            if (!File.Exists(path)) return Array.Empty<string>();

            // Read all: MaxLines is small; keep it simple.
            var lines = File.ReadAllLines(path);
            return lines.Where(l => !string.IsNullOrWhiteSpace(l))
                .Skip(Math.Max(0, lines.Length - count))
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string GetHistoryPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "LL", "history.txt");
    }

    private static string? TryReadLastLine(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            return lines.Length == 0 ? null : lines[^1];
        }
        catch
        {
            return null;
        }
    }

    private static void TrimIfNeeded(string path)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length <= MaxLines) return;

            var keep = lines.Skip(lines.Length - MaxLines).ToArray();
            File.WriteAllLines(path, keep);
        }
        catch
        {
        }
    }
}
