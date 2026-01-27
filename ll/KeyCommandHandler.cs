using System.Linq;
using LL;

namespace LL;

public static class KeyCommandHandler
{
    public static void Handle(string[] args)
    {
        if (!KeyManager.IsLoaded)
        {
            KeyManager.LoadKeys();
        }

        if (args.Length == 0)
        {
            UI.PrintError("用法: key <子命令> [参数]");
            UI.PrintInfo("子命令: add <name> <value>, get <name>, list, remove <name>, import <csv_file>, search <keyword>");
            return;
        }

        string subCommand = args[0].ToLower();
        string[] subArgs = args.Skip(1).ToArray();

        switch (subCommand)
        {
            case "add":
                if (subArgs.Length < 2)
                {
                    UI.PrintError("用法: key add <name> <value>");
                    return;
                }
                KeyManager.AddKey(subArgs[0], string.Join(" ", subArgs.Skip(1)));
                UI.PrintSuccess("密钥已添加。");
                break;
            case "get":
                if (subArgs.Length < 1 || subArgs.Length > 2)
                {
                    UI.PrintError("用法: key get <name> [123456]");
                    return;
                }
                string name = subArgs[0];
                if (subArgs.Length > 1 && subArgs[1] == "123456")
                {
                    string value = KeyManager.GetKey(name);
                    string decrypted = SM4Helper.Decrypt(value);
                    if (decrypted == null)
                    {
                        UI.PrintError($"密钥 '{name}' 不存在。");
                    }
                    else
                    {
                        Console.WriteLine(decrypted);
                    }
                }
                else
                {
                    string value = KeyManager.GetKey(name);
                    if (value == null)
                    {
                        UI.PrintError($"密钥 '{name}' 不存在。");
                    }
                    else
                    {
                        Console.WriteLine(value);
                    }
                }
                break;
            case "list":
                var keys = KeyManager.ListKeys();
                if (keys.Any())
                {
                    if (subArgs.Length > 0 && subArgs[0] == "123456")
                    {
                        Console.WriteLine("可用密钥 (密文):");
                        foreach (var k in keys)
                        {
                            string val = KeyManager.GetKey(k);
                            string decrypted = SM4Helper.Decrypt(val);
                            Console.WriteLine($"{k}: {decrypted}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("可用密钥 (明文):");
                        foreach (var k in keys)
                        {
                            string val = KeyManager.GetKey(k);
                            Console.WriteLine($"{k}: {val}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("无可用密钥。");
                }
                break;
            case "remove":
                if (subArgs.Length < 1)
                {
                    UI.PrintError("用法: key remove <name>");
                    return;
                }
                if (KeyManager.RemoveKey(subArgs[0]))
                {
                    UI.PrintSuccess("密钥已移除。");
                }
                else
                {
                    UI.PrintError("密钥不存在。");
                }
                break;
            case "import":
                if (subArgs.Length < 1)
                {
                    UI.PrintError("用法: key import <csv_file>");
                    return;
                }
                KeyManager.ImportFromCSV(subArgs[0]);
                UI.PrintSuccess("CSV 已导入。");
                break;
            case "search":
                if (subArgs.Length < 1)
                {
                    UI.PrintError("用法: key search <keyword>");
                    return;
                }
                bool reveal = subArgs.Length > 1 && subArgs[1] == "123456";
                var results = KeyManager.SearchKeys(subArgs[0], reveal);
                if (results.Any())
                {
                    UI.PrintInfo("搜索结果:");
                    foreach (var (key, username, password) in results)
                    {
                        if (reveal)
                        {
                            UI.PrintResult(key, $"{username}:{password}");
                        }
                        else
                        {
                            UI.PrintResult(key, username); // username holds encrypted blob when not revealed
                        }
                    }
                }
                else
                {
                    UI.PrintInfo("未找到匹配的密钥。");
                }
                break;
            default:
                UI.PrintError($"未知子命令: {subCommand}");
                break;
        }
    }
}