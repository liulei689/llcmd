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

    public static string Encrypt(string plainText)
    {
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
        var keyBytes = Encoding.UTF8.GetBytes(_key).AsSpan(0, 16).ToArray();
        var engine = new SM4Engine();
        var blockCipher = new CbcBlockCipher(engine);
        var cipher = new PaddedBufferedBlockCipher(blockCipher, new Pkcs7Padding());
        var keyParam = new KeyParameter(keyBytes);
        var iv = new byte[16];
        var ivParam = new ParametersWithIV(keyParam, iv);
        cipher.Init(true, ivParam);

        var outputBytes = new byte[cipher.GetOutputSize(plainBytes.Length)];
        var len = cipher.ProcessBytes(plainBytes, 0, plainBytes.Length, outputBytes, 0);
        cipher.DoFinal(outputBytes, len);

        return outputBytes;
    }

    public static byte[] DecryptBytes(byte[] cipherBytes)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_key).AsSpan(0, 16).ToArray();
        var engine = new SM4Engine();
        var blockCipher = new CbcBlockCipher(engine);
        var cipher = new PaddedBufferedBlockCipher(blockCipher, new Pkcs7Padding());
        var keyParam = new KeyParameter(keyBytes);
        var iv = new byte[16];
        var ivParam = new ParametersWithIV(keyParam, iv);
        cipher.Init(false, ivParam);

        var outputBytes = new byte[cipher.GetOutputSize(cipherBytes.Length)];
        var len = cipher.ProcessBytes(cipherBytes, 0, cipherBytes.Length, outputBytes, 0);
        var finalLen = cipher.DoFinal(outputBytes, len);

        return outputBytes.AsSpan(0, len + finalLen).ToArray();
    }
}