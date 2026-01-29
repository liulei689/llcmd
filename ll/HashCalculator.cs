using System;
using System.Security.Cryptography;
using System.Text;
using LL;

namespace LL;

public static class HashCalculator
{
    public static void Handle(string[] args)
    {
        if (args.Length < 2 || args[0] == "help")
        {
            UI.PrintInfo("用法:");
            UI.PrintInfo("  hash <algorithm> <text>");
            UI.PrintInfo("  hash <algorithm> --file <file_path>");
            UI.PrintInfo("支持算法: md5, sha1, sha256, sha384, sha512");
            return;
        }

        string algorithm = args[0].ToLower();
        string text = null;
        string filePath = null;

        if (args.Length >= 3 && args[1] == "--file")
        {
            filePath = args[2];
        }
        else
        {
            text = string.Join(" ", args.Skip(1));
        }

        byte[] data;
        if (filePath != null)
        {
            if (!File.Exists(filePath))
            {
                UI.PrintError("文件不存在。");
                return;
            }
            data = File.ReadAllBytes(filePath);
        }
        else if (text != null)
        {
            data = Encoding.UTF8.GetBytes(text);
        }
        else
        {
            UI.PrintError("请提供文本或文件路径。");
            return;
        }

        string hash = ComputeHash(algorithm, data);
        if (hash != null)
        {
            UI.PrintSuccess($"{algorithm.ToUpper()} 哈希: {hash}");
        }
        else
        {
            UI.PrintError("不支持的算法。");
        }
    }

    private static string ComputeHash(string algorithm, byte[] data)
    {
        using (HashAlgorithm hashAlg = algorithm switch
        {
            "md5" => MD5.Create(),
            "sha1" => SHA1.Create(),
            "sha256" => SHA256.Create(),
            "sha384" => SHA384.Create(),
            "sha512" => SHA512.Create(),
            _ => null
        })
        {
            if (hashAlg == null) return null;
            byte[] hashBytes = hashAlg.ComputeHash(data);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        }
    }
}