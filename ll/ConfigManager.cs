using System.Text.Json;
using System.Text.Json.Nodes;
using LL;
using System.Text.Json.Serialization;

namespace LL;

public static class ConfigManager
{
    private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");

    /// <summary>
    /// 读取配置值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="keyPath">键路径，如 "Database:Username"</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>配置值</returns>
    public static T GetValue<T>(string keyPath, T defaultValue = default)
    {
        try
        {
            if (!File.Exists(ConfigPath)) return defaultValue;
            var json = File.ReadAllText(ConfigPath);
            var node = JsonNode.Parse(json);
            var keys = keyPath.Split(':');
            JsonNode current = node;
            foreach (var key in keys)
            {
                if (current is JsonObject obj && obj.TryGetPropertyValue(key, out current))
                {
                    continue;
                }
                return defaultValue;
            }
            if (current != null)
            {
                return current.GetValue<T>();
            }
            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// 设置配置值
    /// </summary>
    /// <param name="keyPath">键路径，如 "Database:Username"</param>
    /// <param name="value">新值</param>
    public static void SetValue(string keyPath, object value)
    {
        try
        {
            JsonNode node;
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                node = JsonNode.Parse(json);
            }
            else
            {
                node = new JsonObject();
            }

            var keys = keyPath.Split(':');
            JsonNode current = node;
            for (int i = 0; i < keys.Length - 1; i++)
            {
                var key = keys[i];
                if (current is JsonObject obj)
                {
                    if (!obj.ContainsKey(key))
                    {
                        obj[key] = new JsonObject();
                    }
                    current = obj[key];
                }
            }

            if (current is JsonObject obj2)
            {
                obj2[keys.Last()] = JsonValue.Create(value);
            }

            var options = new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = JsonSerializerOptions.Default.TypeInfoResolver };
            var newJson = node.ToJsonString(options);
            File.WriteAllText(ConfigPath, newJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"更新配置错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取整个配置对象
    /// </summary>
    /// <returns>配置对象</returns>
    public static JsonObject GetConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new JsonObject();
            var json = File.ReadAllText(ConfigPath);
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }
}