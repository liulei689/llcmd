using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LL;

/// <summary>
/// 面部识别认证模块 - 调用外部程序进行身份验证
/// </summary>
public static class FaceAuthCommands
{
    // 认证状态缓存
    private static FaceAuthResult? _lastAuthResult;
    private static DateTime _lastAuthTime;
    private static readonly TimeSpan AuthValidDuration = TimeSpan.FromMinutes(5); // 认证有效期5分钟

    /// <summary>
    /// 认证结果模型
    /// </summary>
    public class FaceAuthResult
    {
        public bool Success { get; set; }
        public string UserName { get; set; } = "";
        public int UserId { get; set; }
        public string AuthId { get; set; } = "";
        public string AuthTime { get; set; } = "";
        public string ErrorMessage { get; set; } = "";

        public string ToJson()
        {
            // AOT 兼容的手动 JSON 构建
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"success\": {Success.ToString().ToLower()},");
            sb.AppendLine($"  \"userName\": \"{EscapeJson(UserName)}\",");
            sb.AppendLine($"  \"userId\": {UserId},");
            sb.AppendLine($"  \"authId\": \"{EscapeJson(AuthId)}\",");
            sb.AppendLine($"  \"authTime\": \"{EscapeJson(AuthTime)}\",");
            sb.AppendLine($"  \"errorMessage\": \"{EscapeJson(ErrorMessage)}\"");
            sb.Append("}");
            return sb.ToString();
        }

        private static string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\n", "\\n")
                      .Replace("\r", "\\r")
                      .Replace("\t", "\\t");
        }
    }

    /// <summary>
    /// 执行面部识别认证
    /// 用法: faceauth [userid]  默认userid为1001
    /// </summary>
    public static void Execute(string[] args)
    {
        int userId = 1001; // 默认用户ID
        if (args.Length > 0 && int.TryParse(args[0], out int parsedId))
        {
            userId = parsedId;
        }

        var result = Authenticate(userId);
        
        // 显示JSON结果
        UI.PrintHeader("面部识别认证结果");
        Console.WriteLine(result.ToJson());
        
        if (result.Success)
        {
            UI.PrintSuccess($"认证成功！欢迎, {result.UserName}");
        }
        else
        {
            UI.PrintError($"认证失败: {result.ErrorMessage}");
        }
    }

    /// <summary>
    /// 单独调用面部识别，返回认证结果（供其他模块调用）
    /// </summary>
    public static FaceAuthResult Authenticate(int userId = 1001)
    {
        var result = new FaceAuthResult();
        string exePath = Path.Combine(AppContext.BaseDirectory, "face", "CheckInProject.App.exe");

        if (!File.Exists(exePath))
        {
            result.ErrorMessage = $"面部识别程序未找到: {exePath}";
            result.AuthTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _lastAuthResult = result;
            _lastAuthTime = DateTime.Now;
            return result;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--auth {userId}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.ErrorMessage = "无法启动面部识别程序";
                result.AuthTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                _lastAuthResult = result;
                _lastAuthTime = DateTime.Now;
                return result;
            }

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000); // 最多等待30秒

            // 解析输出结果
            result = ParseAuthOutput(output, userId);
            
            if (!string.IsNullOrEmpty(error) && !result.Success)
            {
                result.ErrorMessage = string.IsNullOrEmpty(result.ErrorMessage) ? error : result.ErrorMessage;
            }

            // 缓存认证结果
            _lastAuthResult = result;
            _lastAuthTime = DateTime.Now;

            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"执行异常: {ex.Message}";
            result.AuthTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _lastAuthResult = result;
            _lastAuthTime = DateTime.Now;
            return result;
        }
    }

    /// <summary>
    /// 解析认证程序的输出
    /// 期望格式: true/false 或其他包含成功/失败信息的文本
    /// </summary>
    private static FaceAuthResult ParseAuthOutput(string output, int userId)
    {
        var result = new FaceAuthResult
        {
            UserId = userId,
            AuthTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            AuthId = Guid.NewGuid().ToString() // 生成临时AuthId
        };

        if (string.IsNullOrWhiteSpace(output))
        {
            result.ErrorMessage = "面部识别程序无输出";
            return result;
        }

        string trimmedOutput = output.Trim();

        // 检查是否包含 "true"（不区分大小写）
        if (trimmedOutput.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            trimmedOutput.Contains("true", StringComparison.OrdinalIgnoreCase))
        {
            result.Success = true;
            // 尝试从输出中提取用户名等信息
            ExtractUserInfo(trimmedOutput, result);
        }
        else if (trimmedOutput.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                 trimmedOutput.Contains("false", StringComparison.OrdinalIgnoreCase))
        {
            result.Success = false;
            result.ErrorMessage = "面部识别验证失败";
        }
        else
        {
            // 尝试解析为JSON格式（手动解析，AOT兼容）
            var parsedJson = TryParseJson(trimmedOutput);
            if (parsedJson != null)
            {
                return parsedJson;
            }

            // 未知格式，假设失败
            result.Success = false;
            result.ErrorMessage = $"无法识别的输出: {trimmedOutput.Substring(0, Math.Min(100, trimmedOutput.Length))}";
        }

        return result;
    }

    /// <summary>
    /// 尝试手动解析 JSON（AOT兼容）
    /// </summary>
    private static FaceAuthResult? TryParseJson(string json)
    {
        try
        {
            var result = new FaceAuthResult();
            
            // 提取 success
            var successMatch = Regex.Match(json, "\"success\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            if (successMatch.Success)
            {
                result.Success = successMatch.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                return null; // 不是有效的JSON格式
            }

            // 提取 userName
            var userNameMatch = Regex.Match(json, "\"userName\"\\s*:\\s*\"([^\"]*)\"");
            if (userNameMatch.Success) result.UserName = userNameMatch.Groups[1].Value;

            // 提取 userId
            var userIdMatch = Regex.Match(json, "\"userId\"\\s*:\\s*(\\d+)");
            if (userIdMatch.Success && int.TryParse(userIdMatch.Groups[1].Value, out int uid))
                result.UserId = uid;

            // 提取 authId
            var authIdMatch = Regex.Match(json, "\"authId\"\\s*:\\s*\"([^\"]*)\"");
            if (authIdMatch.Success) result.AuthId = authIdMatch.Groups[1].Value;

            // 提取 authTime
            var authTimeMatch = Regex.Match(json, "\"authTime\"\\s*:\\s*\"([^\"]*)\"");
            if (authTimeMatch.Success) result.AuthTime = authTimeMatch.Groups[1].Value;

            // 提取 errorMessage
            var errorMatch = Regex.Match(json, "\"errorMessage\"\\s*:\\s*\"([^\"]*)\"");
            if (errorMatch.Success) result.ErrorMessage = errorMatch.Groups[1].Value;

            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从输出中提取用户信息
    /// </summary>
    private static void ExtractUserInfo(string output, FaceAuthResult result)
    {
        // 尝试匹配用户名（格式: "UserName: xxx" 或 "用户名: xxx"）
        var userNameMatch = Regex.Match(output, @"(?:UserName|用户名)[""\s]*[:：][""\s]*([^\n\r""]+)", RegexOptions.IgnoreCase);
        if (userNameMatch.Success)
        {
            result.UserName = userNameMatch.Groups[1].Value.Trim();
        }
        else
        {
            result.UserName = $"用户{result.UserId}";
        }

        // 尝试匹配AuthId（格式: "AuthId: xxx" 或 "权限ID: xxx"）
        var authIdMatch = Regex.Match(output, @"(?:AuthId|权限ID|StudentID)[""\s]*[:：][""\s]*([a-fA-F0-9\-]+)", RegexOptions.IgnoreCase);
        if (authIdMatch.Success)
        {
            string parsedId = authIdMatch.Groups[1].Value.Trim();
            if (Guid.TryParse(parsedId, out _))
            {
                result.AuthId = parsedId;
            }
        }
    }

    /// <summary>
    /// 检查当前是否有有效的认证
    /// </summary>
    public static bool HasValidAuth()
    {
        if (_lastAuthResult == null || !_lastAuthResult.Success)
            return false;

        // 检查认证是否过期
        return DateTime.Now - _lastAuthTime < AuthValidDuration;
    }

    /// <summary>
    /// 获取最后一次认证结果
    /// </summary>
    public static FaceAuthResult? GetLastAuthResult()
    {
        return _lastAuthResult;
    }

    /// <summary>
    /// 要求认证（用于受保护的命令）
    /// 返回true表示认证通过，false表示认证失败或取消
    /// </summary>
    public static bool RequireAuth(string commandName)
    {
        if (HasValidAuth())
        {
            UI.PrintInfo($"[{commandName}] 使用缓存的认证信息");
            return true;
        }

        UI.PrintHeader($"需要面部识别认证 - [{commandName}]");
        UI.PrintInfo("正在启动面部识别...");

        var result = Authenticate();
        
        if (result.Success)
        {
            UI.PrintSuccess($"认证成功！欢迎, {result.UserName}");
            return true;
        }
        else
        {
            UI.PrintError($"认证失败: {result.ErrorMessage}");
            return false;
        }
    }

    /// <summary>
    /// 清除认证缓存
    /// </summary>
    public static void ClearAuth()
    {
        _lastAuthResult = null;
        _lastAuthTime = DateTime.MinValue;
        UI.PrintInfo("认证缓存已清除");
    }

    /// <summary>
    /// 显示当前认证状态
    /// </summary>
    public static void ShowStatus()
    {
        UI.PrintHeader("面部识别认证状态");
        
        if (_lastAuthResult == null)
        {
            UI.PrintInfo("尚未进行过认证");
            return;
        }

        UI.PrintResult("认证状态", _lastAuthResult.Success ? "成功" : "失败");
        UI.PrintResult("用户名", _lastAuthResult.UserName);
        UI.PrintResult("用户ID", _lastAuthResult.UserId.ToString());
        UI.PrintResult("权限ID", _lastAuthResult.AuthId);
        UI.PrintResult("认证时间", _lastAuthResult.AuthTime);
        
        bool isValid = HasValidAuth();
        UI.PrintResult("有效期状态", isValid ? "有效" : "已过期");
        
        if (!isValid && _lastAuthResult.Success)
        {
            TimeSpan elapsed = DateTime.Now - _lastAuthTime;
            UI.PrintInfo($"认证已过期 {elapsed.TotalMinutes:F1} 分钟");
        }

        if (!string.IsNullOrEmpty(_lastAuthResult.ErrorMessage))
        {
            UI.PrintResult("错误信息", _lastAuthResult.ErrorMessage);
        }
    }
}
