using System.Diagnostics;

namespace LL;

public static class ShortcutManager
{
    private static readonly Dictionary<string, (string Description, string Path)> _shortcuts = new();

    public static void RegisterShortcut(string alias, string desc, string path)
    {
        _shortcuts[alias.ToLower()] = (desc, path);
    }

    public static void OpenProgram(string[] args)
    {
        if (args.Length == 0)
        {
            UI.PrintError("请指定要打开的程序名称");
            UI.PrintInfo("示例: o vs, o code, o notepad");
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
            UI.PrintInfo("输入 'list' 查看支持的程序列表");
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
        UI.PrintHeader("程序清单 (Use 'o' to launch)");
        foreach (var sc in _shortcuts)
        {
            UI.PrintItem(sc.Key, $"{sc.Value.Description}");
        }
    }
}
