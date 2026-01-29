using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LL;

namespace LL;

public static class KeyManager
{
    private static readonly string KeysPath = Path.Combine(AppContext.BaseDirectory, "keys.llk");
    private static Dictionary<string, string> _keyValues = new(); // key -> encrypted value
    private static bool _isLoaded = false;
    private static string _keyA;
    private static int _version;
    private static DateTime _ttl;
    private static bool _keysLoaded = false;

    public static bool IsLoaded => _keysLoaded;
    public static bool IsExpired => DateTime.Now > _ttl;

    public static void LoadKeys()
    {
        if (_keysLoaded) return;

        _keyA = ConfigManager.GetValue("keyA", "");
        _version = int.TryParse(ConfigManager.GetValue("keyVersion", "1"), out int v) ? v : 1;
        string ttlStr = ConfigManager.GetValue("keyTtl", "");
        if (!string.IsNullOrEmpty(ttlStr) && DateTime.TryParse(ttlStr, out DateTime ttl))
        {
            _ttl = ttl;
        }
        else
        {
            _ttl = DateTime.MinValue;
        }

        if (string.IsNullOrEmpty(_keyA))
        {
            GenerateInitialKeys();
            return;
        }

        // Set SM4 to use keyA
        SM4Helper.SetKey(_keyA);

        // Load the keys file
        LoadKeysFile();

        _keysLoaded = true;
    }

    private static void GenerateInitialKeys()
    {
        _keyA = GenerateRandomKey();
        _version = 1;
        double ttlHours = double.TryParse(ConfigManager.GetValue("keyTtlHours", "0.01"), out double h) ? h : 0.01;
        _ttl = DateTime.Now.AddHours(ttlHours);

        // Save to config (keyA in plain text)
        ConfigManager.SetValue("keyA", _keyA);
        ConfigManager.SetValue("keyVersion", _version.ToString());
        ConfigManager.SetValue("keyTtl", _ttl.ToString("o")); // ISO format

        // Set SM4 to keyA
        SM4Helper.SetKey(_keyA);

        _keysLoaded = true;
    }

    private static string GenerateRandomKey()
    {
        var bytes = new byte[12];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static void LoadKeysFile()
    {
        if (!File.Exists(KeysPath)) return;
        try
        {
            var encryptedBytes = SM4Helper.DecryptBytes(File.ReadAllBytes(KeysPath));
            using (var ms = new MemoryStream(encryptedBytes))
            using (var reader = new BinaryReader(ms))
            {
                string header = reader.ReadString();
                if (header != "llk") return;
                int version = reader.ReadInt32();
                if (version != 1) return; // CurrentVersion
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
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

    private static void SaveKeysFile()
    {
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write("llk");
            writer.Write(1); // CurrentVersion
            writer.Write(_keyValues.Count);
            foreach (var kvp in _keyValues)
            {
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
        if (IsExpired)
        {
            UI.PrintError("Key A 已过期，无法添加新密钥。请先运行 key refresh");
            return;
        }
        string encryptedValue = SM4Helper.Encrypt(value);
        _keyValues[name] = encryptedValue;
        SaveKeysFile();
    }

    public static string GetKey(string name)
    {
        if (_keyValues.TryGetValue(name, out string encryptedValue))
        {
            return encryptedValue;
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
            SaveKeysFile();
            return true;
        }
        return false;
    }

    public static void ImportFromCSV(string csvFile)
    {
        if (IsExpired)
        {
            UI.PrintError("Key A 已过期，无法导入。请先运行 key refresh");
            return;
        }
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

    public static void RefreshKeys()
    {
        if (!_keysLoaded) 
        {
            UI.PrintError("密钥未加载，无法刷新。请检查密钥数据。");
            return;
        }

        string newA = GenerateRandomKey();
        int newVersion = _version + 1;
        double ttlHours = double.TryParse(ConfigManager.GetValue("keyTtlHours", "0.01"), out double h) ? h : 0.01;
        DateTime newTtl = DateTime.Now.AddHours(ttlHours);

        // Save to config (newA in plain text)
        ConfigManager.SetValue("keyA", newA);
        ConfigManager.SetValue("keyVersion", newVersion.ToString());
        ConfigManager.SetValue("keyTtl", newTtl.ToString("o"));

        // Zero old key
        if (!string.IsNullOrEmpty(_keyA))
            CryptographicOperations.ZeroMemory(Encoding.UTF8.GetBytes(_keyA));

        // Set new
        _keyA = newA;
        _version = newVersion;
        _ttl = newTtl;
        SM4Helper.SetKey(_keyA);

        // Re-encrypt the keys file with new Key A
        SaveKeysFile();

        _isLoaded = true;

        UI.PrintSuccess($"密钥已刷新，版本: {_version}");
    }

    public static IEnumerable<(string key, string username, string password)> SearchKeys(string keyword, bool reveal)
    {
        var results = new List<(string, string, string)>();
        foreach (var kvp in _keyValues)
        {
            if (kvp.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                string value = reveal ? SM4Helper.Decrypt(kvp.Value) : kvp.Value;
                var parts = value.Split(':');
                string username = parts.Length > 0 ? parts[0] : "";
                string password = parts.Length > 1 ? parts[1] : "";
                results.Add((kvp.Key, username, password));
            }
        }
        return results;
    }
}