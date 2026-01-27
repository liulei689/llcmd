using System.Collections.Generic;
using System.IO;
using LL;

namespace LL;

public static class KeyManager
{
    private static readonly string KeysPath = Path.Combine(AppContext.BaseDirectory, "keys.llk");
    private static Dictionary<string, string> _keyValues = new(); // key -> encrypted value
    private static bool _isLoaded = false;
    private const int CurrentVersion = 1;

    public static bool IsLoaded => _isLoaded;

    public static void LoadKeys()
    {
        if (!File.Exists(KeysPath)) return;
        try
        {
            var encryptedBytes = File.ReadAllBytes(KeysPath);
            var plainBytes = SM4Helper.DecryptBytes(encryptedBytes);
            using (var ms = new MemoryStream(plainBytes))
            using (var reader = new BinaryReader(ms))
            {
                string header = reader.ReadString();
                if (header != "llk") return;
                int version = reader.ReadInt32();
                if (version != CurrentVersion) return;
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    // key stored in plaintext inside the encrypted file; value is stored encrypted
                    string key = reader.ReadString();
                    string encryptedValue = reader.ReadString();
                    _keyValues[key] = encryptedValue;
                }
            }
        }
        catch
        {
        }
        _isLoaded = true;
    }

    public static void SaveKeys()
    {
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write("llk");
            writer.Write(CurrentVersion);
            writer.Write(_keyValues.Count);
            foreach (var kvp in _keyValues)
            {
                // key kept as plaintext in the record (the whole file is encrypted on disk)
                writer.Write(kvp.Key);
                writer.Write(kvp.Value);
            }
            var plainBytes = ms.ToArray();
            var encrypted = SM4Helper.EncryptBytes(plainBytes);
            File.WriteAllBytes(KeysPath, encrypted);
        }
    }

    public static void AddKey(string name, string value)
    {
        // 值先使用类级别加密（密文存储），键名也在保存时加密
        string encryptedValue = SM4Helper.Encrypt(value);
        _keyValues[name] = encryptedValue;
        SaveKeys();
    }

    public static string GetKey(string name)
    {
        if (_keyValues.TryGetValue(name, out string encryptedValue))
        {
            return encryptedValue; // 返回加密的值，只有在上层传入 123456 参数时才会解密输出
        }
        return null;
    }

    public static IEnumerable<string> ListKeys()
    {
        return _keyValues.Keys;
    }

    public static bool RemoveKey(string name)
    {
        if (_keyValues.Remove(name))
        {
            SaveKeys();
            return true;
        }
        return false;
    }

    public static void ImportFromCSV(string csvFile)
    {
        if (!File.Exists(csvFile)) return;
        var lines = File.ReadAllLines(csvFile);
        for (int i = 1; i < lines.Length; i++) // 跳过标题
        {
            var parts = lines[i].Split(',');
            if (parts.Length >= 5)
            {
                string name = parts[0].Trim('"');
                string url = parts[1].Trim('"');
                string username = parts[2].Trim('"');
                string password = parts[3].Trim('"');
                string note = parts[4].Trim('"');
                string key = $"{name}|{url}|{note}";
                string value = $"{username}|{password}";
                AddKey(key, value);
            }
        }
    }

    public static List<(string key, string username, string password)> SearchKeys(string keyword, bool reveal = false)
    {
        var results = new List<(string, string, string)>();
        foreach (var kvp in _keyValues)
        {
            string key = kvp.Key;
            if (key.Contains(keyword, StringComparison.OrdinalIgnoreCase)) { 

                    string encryptedValue = GetKey(key);
                if (encryptedValue != null)
                {
                    if (reveal)
                    {
                        try
                        {
                            string plainValue = SM4Helper.Decrypt(encryptedValue);
                            var valueParts = plainValue.Split('|');
                            if (valueParts.Length >= 2)
                            {
                                results.Add((key, valueParts[0], valueParts[1]));
                            }
                            else results.Add((key, plainValue, string.Empty));
                        }
                        catch
                        {
                            // skip if cannot decrypt
                        }
                    }
                    else
                    {
                        // not revealing: return encrypted blob in username field, leave password empty
                        results.Add((key, encryptedValue, string.Empty));
                    }
                }
                
            }
        }
        return results;
    }
}