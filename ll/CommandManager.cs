namespace LL;

public static class CommandManager
{
    private static readonly Dictionary<string, (string Description, Action<string[]> Action)> _commands = new();
    private static readonly List<string> _commandOrder = new();
    private static readonly Dictionary<int, string> _idToCommand = new();
    private static readonly Dictionary<string, int> _commandToId = new();
    private static readonly Dictionary<int, HashSet<string>> _idAliases = new();

    public static void RegisterCommand(int id, string name, string desc, Action<string[]> action)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "id 必须大于 0");
        var key = name.ToLowerInvariant();
        _commands[key] = (desc, action);

        // Ensure it appears in listing order (first time only)
        if (!_commandOrder.Contains(key))
            _commandOrder.Add(key);

        // Remove previous id mapping if any
        if (_commandToId.TryGetValue(key, out var oldId))
            _idToCommand.Remove(oldId);

        _commandToId[key] = id;
        _idToCommand[id] = _idToCommand.TryGetValue(id, out var existing) ? existing : key; // keep first as primary

        if (!_idAliases.TryGetValue(id, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _idAliases[id] = set;
        }
        set.Add(key);
    }

    public static void RegisterCommand(string name, string desc, Action<string[]> action)
    {
        var key = name.ToLowerInvariant();
        _commands[key] = (desc, action);

        // assign numeric id on first registration only (fallback)
        if (!_commandOrder.Contains(key))
            _commandOrder.Add(key);

        if (!_commandToId.ContainsKey(key))
        {
            int id = NextAutoId();
            _idToCommand[id] = key;
            _commandToId[key] = id;
        }
    }

    private static int NextAutoId()
    {
        // Smallest positive integer not used
        int id = 1;
        while (_idToCommand.ContainsKey(id)) id++;
        return id;
    }

    public static void ExecuteCommand(string cmdInput, string[] args)
    {
        var raw = cmdInput.Trim();

        // numeric alias (manual mapping only)
        if (int.TryParse(raw, out var id) && _idToCommand.TryGetValue(id, out var mapped))
        {
            raw = mapped;
        }

        string cmd = raw.ToLowerInvariant();

        if (_commands.TryGetValue(cmd, out var commandInfo))
        {
            commandInfo.Action(args);
            return;
        }

        UI.PrintError($"未知指令: '{cmd}'");
        UI.PrintInfo("输入 'list' 查看可用指令。");
    }

    public static void ShowCommands()
    {
        UI.PrintHeader("系统功能指令");

        // Show numbered commands first (sorted by id). Avoid duplicating aliases.
        foreach (var kv in _idToCommand.OrderBy(kv => kv.Key))
        {
            var id = kv.Key;
            var primary = kv.Value;
            if (!_commands.TryGetValue(primary, out var v)) continue;
            UI.PrintItem($"{id,3}  {primary}", v.Description);
        }

        // Then show non-numbered commands (if any)
        foreach (var cmd in _commands.Keys.OrderBy(k => k))
        {
            if (_commandToId.ContainsKey(cmd)) continue;
            UI.PrintItem($"     {cmd}", _commands[cmd].Description);
        }
    }
}
