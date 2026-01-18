namespace LL;

public static class TaskManager
{
    private static readonly object _lock = new();
    private static CancellationTokenSource? _cts;
    private static string? _name;
    private static DateTime? _startedAt;

    public static void Register(string name, CancellationTokenSource cts)
    {
        lock (_lock)
        {
            _cts = cts;
            _name = name;
            _startedAt = DateTime.Now;
        }
    }

    public static void Clear(CancellationTokenSource cts)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_cts, cts))
                return;

            _cts = null;
            _name = null;
            _startedAt = null;
        }
    }

    public static void CancelLatest()
    {
        CancellationTokenSource? cts;
        string? name;

        lock (_lock)
        {
            cts = _cts;
            name = _name;
        }

        if (cts is null)
        {
            // Fallback for legacy/background tasks that didn't register yet.
            if (PowerManager.CurrentMode is not null)
            {
                PowerManager.CancelTask();
                return;
            }

            UI.PrintInfo("当前没有可取消的任务。");
            return;
        }

        if (cts.IsCancellationRequested)
        {
            UI.PrintInfo($"任务已在取消中: {name}");
            return;
        }

        try
        {
            cts.Cancel();
            UI.PrintSuccess($"已取消任务: {name}");
        }
        catch
        {
            UI.PrintError("取消任务失败。");
        }
    }

    public static (string? Name, DateTime? StartedAt) GetLatest()
    {
        lock (_lock)
        {
            return (_name, _startedAt);
        }
    }
}
