using System.Collections.Generic;
using System.IO;
using LL;

namespace LL;

public static class KeyManager
{
    private static readonly string KeysPath = Path.Combine(AppContext.BaseDirectory, "keys.llk");
    private static HashSet<string> _keys = new(); // only keys
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
                if (header != "llk") return; // 头部不匹配，忽略
                int version = reader.ReadInt32();
                if (version != CurrentVersion) return; // 版本不匹配，忽略
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string key = reader.ReadString();
                    reader.ReadString(); // skip encrypted value
                    _keys.Add(key);
                }
            }
        }
        catch
        {
            // 忽略加载失败
        }
        _isLoaded = true;
    }

    public static void SaveKeys()
    {
        // 需要重新生成数据，从现有文件读取所有键值，加上新添加的，但复杂。
        // 为了简单，假设添加时重新读取并添加。
        // 但这里简化，重新生成空或从 _keys，但没有值。
        // 实际上，SaveKeys 需要所有键值，所以需要缓存值或实时。
        // 或许保持 Dictionary，但用户说只缓存key。
        // 为了实现，修改为实时保存所有。
        // 这里简化，SaveKeys 什么都不做，AddKey 时重新写文件。
    }

    public static void AddKey(string name, string value)
    {
        // 读取现有，添加新，保存
        var allKeys = new Dictionary<string, string>();
        if (File.Exists(KeysPath))
        {
            try
            {
                var encryptedBytes = File.ReadAllBytes(KeysPath);
                var plainBytes = SM4Helper.DecryptBytes(encryptedBytes);
                using (var ms = new MemoryStream(plainBytes))
                using (var reader = new BinaryReader(ms))
                {
                    string header = reader.ReadString();
                    if (header == "llk")
                    {
                        int version = reader.ReadInt32();
                        if (version == CurrentVersion)
                        {
                            int count = reader.ReadInt32();
                            for (int i = 0; i < count; i++)
                            {
                                string key = reader.ReadString();
                                string encryptedValue = reader.ReadString();
                                allKeys[key] = encryptedValue;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }
        allKeys[name] = SM4Helper.Encrypt(value);
        _keys = new HashSet<string>(allKeys.Keys);
        // 保存
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write("llk");
            writer.Write(CurrentVersion);
            writer.Write(allKeys.Count);
            foreach (var kvp in allKeys)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value);
            }
            var plainBytes = ms.ToArray();
            var encrypted = SM4Helper.EncryptBytes(plainBytes);
            File.WriteAllBytes(KeysPath, encrypted);
        }
    }

    public static string GetKey(string name)
    {
        if (!_keys.Contains(name)) return null;
        // 实时读取
        if (!File.Exists(KeysPath)) return null;
        try
        {
            var encryptedBytes = File.ReadAllBytes(KeysPath);
            var plainBytes = SM4Helper.DecryptBytes(encryptedBytes);
            using (var ms = new MemoryStream(plainBytes))
            using (var reader = new BinaryReader(ms))
            {
                string header = reader.ReadString();
                if (header != "llk") return null;
                int version = reader.ReadInt32();
                if (version != CurrentVersion) return null;
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string key = reader.ReadString();
                    string encryptedValue = reader.ReadString();
                    if (key == name)
                    {
                        return encryptedValue;
                    }
                }
            }
        }
        catch
        {
        }
        return null;
    }

    public static IEnumerable<string> ListKeys()
    {
        return _keys;
    }

    public static bool RemoveKey(string name)
    {
        if (!_keys.Contains(name)) return false;
        // 重新读取，移除，保存
        var allKeys = new Dictionary<string, string>();
        if (File.Exists(KeysPath))
        {
            try
            {
                var encryptedBytes = File.ReadAllBytes(KeysPath);
                var plainBytes = SM4Helper.DecryptBytes(encryptedBytes);
                using (var ms = new MemoryStream(plainBytes))
                using (var reader = new BinaryReader(ms))
                {
                    string header = reader.ReadString();
                    if (header == "llk")
                    {
                        int version = reader.ReadInt32();
                        if (version == CurrentVersion)
                        {
                            int count = reader.ReadInt32();
                            for (int i = 0; i < count; i++)
                            {
                                string key = reader.ReadString();
                                string encryptedValue = reader.ReadString();
                                if (key != name)
                                {
                                    allKeys[key] = encryptedValue;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }
        _keys = new HashSet<string>(allKeys.Keys);
        // 保存
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write("llk");
            writer.Write(CurrentVersion);
            writer.Write(allKeys.Count);
            foreach (var kvp in allKeys)
            {
                writer.Write(kvp.Key);
                writer.Write(kvp.Value);
            }
            var plainBytes = ms.ToArray();
            var encrypted = SM4Helper.EncryptBytes(plainBytes);
            File.WriteAllBytes(KeysPath, encrypted);
        }
        return true;
    }
}