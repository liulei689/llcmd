using System.Linq;

namespace LL;

internal static class KeyCommandHandler
{
    public static void Handle(string[] args)
    {
        if (!KeyManager.IsLoaded)
        {
            string sm4Key = ConfigManager.GetValue<string>("SM4Key", "");
            SM4Helper.SetKey(sm4Key);
            KeyManager.LoadKeys();
        }

        if (args.Length == 0)
        {
            UI.PrintError("用法: key <子命令> [参数]");
            UI.PrintInfo("可用子命令: add, get, list, help");
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
                    break;
                }
                KeyManager.AddKey(subArgs[0], string.Join(" ", subArgs.Skip(1)));
                UI.PrintSuccess($"密钥 '{subArgs[0]}' 已添加。");
                break;
            case "get":
                if (subArgs.Length < 1 || subArgs.Length > 2)
                {
                    UI.PrintError("用法: key get <name> [123456]");
                    break;
                }
                string name = subArgs[0];
                if (subArgs.Length > 1 && subArgs[1] == "123456")
                {
                    string value = KeyManager.GetKey(name);
                    string encrypted = SM4Helper.Decrypt(value);
                    if (encrypted == null)
                    {
                        UI.PrintError($"密钥 '{name}' 不存在。");
                    }
                    else
                    {
                        Console.WriteLine(encrypted);
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
                            string encrypted = SM4Helper.Decrypt(val);
                            Console.WriteLine($"{k}: {encrypted}");
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
            case "help":
                UI.PrintInfo("用法: key <name> [value]  - 添加或获取密钥\n       key get <name> [123456]  - 获取密钥明文 (输入123456显示密文)\n       key list [123456]  - 列出密钥名和明文 (输入123456显示密文)\n       key del <name>  - 删除密钥");
                break;
            case "del":
                if (subArgs.Length != 1)
                {
                    UI.PrintError("用法: key del <name>");
                    break;
                }
                string delName = subArgs[0];
                if (KeyManager.RemoveKey(delName))
                {
                    UI.PrintSuccess($"密钥 '{delName}' 已删除。");
                }
                else
                {
                    UI.PrintError($"密钥 '{delName}' 不存在。");
                }
                break;
            default:
                UI.PrintError($"未知子命令: {subCommand}");
                break;
        }
    }
}