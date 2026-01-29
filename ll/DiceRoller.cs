using System;
using System.Security.Cryptography;
using LL;

namespace LL;

public static class DiceRoller
{
    public static void Handle(string[] args)
    {
        if (args.Length == 0 || args[0] == "help")
        {
            UI.PrintInfo("用法:");
            UI.PrintInfo("  dice [sides] [count]");
            UI.PrintInfo("掷骰子，默认 6 面，1 次。");
            return;
        }

        int sides = 6;
        int count = 1;

        if (args.Length >= 1 && int.TryParse(args[0], out int s)) sides = s;
        if (args.Length >= 2 && int.TryParse(args[1], out int c)) count = c;

        for (int i = 0; i < count; i++)
        {
            int roll = RandomNumberGenerator.GetInt32(1, sides + 1);
            UI.PrintInfo($"骰子 {i + 1}: {roll}");
        }
    }
}