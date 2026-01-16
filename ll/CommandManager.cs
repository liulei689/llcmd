namespace LL;

public static class CommandManager
{
    private static readonly Dictionary<string, (string Description, Action<string[]> Action)> _commands = new();

    public static void RegisterCommand(string name, string desc, Action<string[]> action)
    {
        _commands[name.ToLower()] = (desc, action);
    }

    public static void ExecuteCommand(string cmdInput, string[] args)
    {
        string cmd = cmdInput.ToLower();

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
        foreach (var cmd in _commands)
        {
            UI.PrintItem(cmd.Key, cmd.Value.Description);
        }
    }
}
