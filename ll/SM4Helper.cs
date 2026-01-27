using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;
using System;
using System.Text;

namespace LL;

public static class SM4Helper
{
    private static string _key = "";

    public static void SetKey(string key)
    {
        _key = (key ?? "").PadRight(16, ' ').Substring(0, 16);
    }

    // 延迟从配置读取密钥（如果未通过 SetKey 设置）
    private static void EnsureKeyLoaded()
    {
        if (!string.IsNullOrEmpty(_key)) return;
        try
        {
            // 使用项目中的 ConfigManager 读取配置项 SM4Key
            var cfgKey = ConfigManager.GetValue("SM4Key", "");
            if (!string.IsNullOrEmpty(cfgKey))
            {
                SetKey(cfgKey);
            }
        }
        catch
        {
            // 忽略读取失败，保持默认空密钥
        }
    }

    public static string Encrypt(string plainText)
    {
        EnsureKeyLoaded();
        var keyBytes = Encoding.UTF8.GetBytes(_key).AsSpan(0, 16).ToArray(); // SM4 key is 128 bits (16 bytes)
        var engine = new SM4Engine();
        var blockCipher = new CbcBlockCipher(engine);
        var cipher = new PaddedBufferedBlockCipher(blockCipher, new Pkcs7Padding());
        var keyParam = new KeyParameter(keyBytes);
        var iv = new byte[16]; // Zero IV
        var ivParam = new ParametersWithIV(keyParam, iv);
        cipher.Init(true, ivParam);

        var inputBytes = Encoding.UTF8.GetBytes(plainText);
        var outputBytes = new byte[cipher.GetOutputSize(inputBytes.Length)];
        var len = cipher.ProcessBytes(inputBytes, 0, inputBytes.Length, outputBytes, 0);
        cipher.DoFinal(outputBytes, len);

        return Convert.ToBase64String(outputBytes);
    }

    public static string Decrypt(string cipherText)
    {
        EnsureKeyLoaded();
        var keyBytes = Encoding.UTF8.GetBytes(_key).AsSpan(0, 16).ToArray();
        var engine = new SM4Engine();
        var blockCipher = new CbcBlockCipher(engine);
        var cipher = new PaddedBufferedBlockCipher(blockCipher, new Pkcs7Padding());
        var keyParam = new KeyParameter(keyBytes);
        var iv = new byte[16];
        var ivParam = new ParametersWithIV(keyParam, iv);
        cipher.Init(false, ivParam);

        var inputBytes = Convert.FromBase64String(cipherText);
        var outputBytes = new byte[cipher.GetOutputSize(inputBytes.Length)];
        var len = cipher.ProcessBytes(inputBytes, 0, inputBytes.Length, outputBytes, 0);
        var finalLen = cipher.DoFinal(outputBytes, len);

        return Encoding.UTF8.GetString(outputBytes, 0, len + finalLen);
    }

    public static byte[] EncryptBytes(byte[] plainBytes)
    {
        EnsureKeyLoaded();
        using (var aes = System.Security.Cryptography.Aes.Create())
        {
            aes.Key = Encoding.UTF8.GetBytes(_key).AsSpan(0, 16).ToArray(); // 128-bit key
            aes.IV = new byte[16]; // Zero IV
            aes.Mode = System.Security.Cryptography.CipherMode.CBC;
            aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
            using (var encryptor = aes.CreateEncryptor())
            {
                return encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            }
        }
    }

    public static byte[] DecryptBytes(byte[] cipherBytes)
    {
        EnsureKeyLoaded();
        using (var aes = System.Security.Cryptography.Aes.Create())
        {
            aes.Key = Encoding.UTF8.GetBytes(_key).AsSpan(0, 16).ToArray();
            aes.IV = new byte[16];
            aes.Mode = System.Security.Cryptography.CipherMode.CBC;
            aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
            using (var decryptor = aes.CreateDecryptor())
            {
                return decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            }
        }
    }
}