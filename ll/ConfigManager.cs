using System.Text.Json;
using System.Text.Json.Nodes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    /// <param name="path">配置文件路径，默认为 config.json</param>
    /// <returns>配置值</returns>
    public static T GetValue<T>(string keyPath, T defaultValue = default, string? path = null)
    {
        string filePath = path ?? ConfigPath;
        try
        {
            if (!File.Exists(filePath)) return defaultValue;
            var json = File.ReadAllText(filePath);
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
    /// <param name="path">配置文件路径，默认为 config.json</param>
    public static void SetValue(string keyPath, object value, string? path = null)
    {
        string filePath = path ?? ConfigPath;
        try
        {
            JsonNode node;
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
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

            // Try using System.Text.Json first
            try
            {
                var newJson = node.ToJsonString();
                File.WriteAllText(filePath, newJson);
                return;
            }
            catch
            {
                // Fallback to Newtonsoft.Json for reliable serialization/formatting
            }

            try
            {
                JObject j;
                if (File.Exists(filePath))
                {
                    j = JObject.Parse(File.ReadAllText(filePath));
                }
                else
                {
                    j = new JObject();
                }

                var parts = keyPath.Split(':');
                JObject cur = j;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    var p = parts[i];
                    if (cur[p] == null || cur[p].Type != JTokenType.Object)
                    {
                        cur[p] = new JObject();
                    }
                    cur = (JObject)cur[p]!;
                }

                var last = parts.Last();
                cur[last] = JToken.FromObject(value);
                File.WriteAllText(filePath, j.ToString(Formatting.Indented));
                return;
            }
            catch (Exception ex2)
            {
                Console.WriteLine($"更新配置错误: {ex2.Message}");
            }
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