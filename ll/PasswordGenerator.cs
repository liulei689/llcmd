using System;
using System.Security.Cryptography;
using System.Text;
using LL;

namespace LL;

public static class PasswordGenerator
{
    public static void Handle(string[] args)
    {
        if (args.Length == 0 || args[0] == "help")
        {
            UI.PrintInfo("用法:");
            UI.PrintInfo("  passwd [length] [--no-symbols] [--no-numbers]");
            UI.PrintInfo("生成随机密码，默认长度 12，包含字母、数字、符号。");
            return;
        }

        int length = 12;
        bool includeSymbols = true;
        bool includeNumbers = true;

        foreach (string arg in args)
        {
            if (int.TryParse(arg, out int l))
            {
                length = l;
            }
            else if (arg == "--no-symbols")
            {
                includeSymbols = false;
            }
            else if (arg == "--no-numbers")
            {
                includeNumbers = false;
            }
        }

        string password = GeneratePassword(length, includeSymbols, includeNumbers);
        UI.PrintSuccess($"生成的密码: {password}");
    }

    private static string GeneratePassword(int length, bool includeSymbols, bool includeNumbers)
    {
        const string letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string numbers = "0123456789";
        const string symbols = "!@#$%^&*()_+-=[]{}|;:,.<>?";

        StringBuilder chars = new StringBuilder(letters);
        if (includeNumbers) chars.Append(numbers);
        if (includeSymbols) chars.Append(symbols);

        byte[] randomBytes = new byte[length];
        RandomNumberGenerator.Fill(randomBytes);

        StringBuilder password = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            int index = randomBytes[i] % chars.Length;
            password.Append(chars[index]);
        }

        return password.ToString();
    }
}