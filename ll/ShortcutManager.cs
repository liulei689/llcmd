using System.Diagnostics;
using System.IO;

namespace LL;

public static class ShortcutManager
{
    private static readonly Dictionary<string, (string Description, string Path)> _shortcuts = new();

    private static readonly Dictionary<int, string> _shortcutIdToAlias = new();

    public static void RegisterShortcut(int id, string alias, string desc, string path)
    {
        RegisterShortcut(alias, desc, path);
        _shortcutIdToAlias[id] = alias.ToLowerInvariant();

        // single-number launch: typing the number will launch directly
        CommandManager.RegisterCommand(id, $"app:{alias.ToLowerInvariant()}", $"启动程序: {desc}", _ =>
        {
            LaunchProgram(desc, path, Array.Empty<string>());
        });
    }

    public static void RegisterShortcut(string alias, string desc, string path)
    {
        _shortcuts[alias.ToLower()] = (desc, path);
    }

    public static void OpenProgram(string[] args)
    {
        if (args.Length == 0)
        {
            UI.PrintError("请指定要打开的程序名称");
            UI.PrintInfo("示例: vs   或   12 vs");
            return;
        }

        string key = args[0].ToLower();
        string[] programArgs = args.Skip(1).ToArray();

        if (_shortcuts.TryGetValue(key, out var shortcutInfo))
        {
            LaunchProgram(shortcutInfo.Description, shortcutInfo.Path, programArgs);
        }
        else
        {
            UI.PrintError($"未找到程序配置: '{key}'");
            UI.PrintInfo("输入 'ls' 查看支持的程序列表");
        }
    }

    private static void LaunchProgram(string name, string path, string[] args)
    {
        UI.PrintInfo($"正在启动 [ {name} ] ...");
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Arguments = string.Join(" ", args)
            };

            // 设置工作目录，解决模拟器等程序找不到配置文件或 ROM 的问题
            if (Path.IsPathRooted(path))
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    psi.WorkingDirectory = dir;
                }
            }

            Process.Start(psi);
            UI.PrintSuccess("启动成功!");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            UI.PrintError($"启动失败: 找不到文件或命令 '{path}'");
            UI.PrintInfo("提示: 请检查路径配置。");
        }
        catch (Exception ex)
        {
            UI.PrintError($"启动错误: {ex.Message}");
        }
    }

    public static void ShowShortcuts()
    {
        UI.PrintHeader("程序清单 (直接输入别名，或输入对应数字)");

        // show those with numbers first
        foreach (var kv in _shortcutIdToAlias.OrderBy(k => k.Key))
        {
            var id = kv.Key;
            var alias = kv.Value;
            if (_shortcuts.TryGetValue(alias, out var v))
                UI.PrintItem($"{id,3}  {alias}", v.Description);
        }

        // show unnumbered shortcuts
        foreach (var sc in _shortcuts.Where(sc => !_shortcutIdToAlias.ContainsValue(sc.Key)).OrderBy(sc => sc.Key))
        {
            UI.PrintItem($"     {sc.Key}", $"{sc.Value.Description}");
        }
    }
}
