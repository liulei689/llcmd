using System;

namespace CheckInProject.App
{
    /// <summary>
    /// 权限验证结果
    /// </summary>
    public class AuthResult
    {
        /// <summary>
        /// 是否验证成功
        /// </summary>
        public bool Success { get; set; }
        
        /// <summary>
        /// 用户名
        /// </summary>
        public string? UserName { get; set; }
        
        /// <summary>
        /// 用户ID
        /// </summary>
        public uint? UserId { get; set; }
        
        /// <summary>
        /// 权限ID
        /// </summary>
        public Guid AuthId { get; set; }
        
        /// <summary>
        /// 验证时间
        /// </summary>
        public DateTime AuthTime { get; set; }
        
        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 转换为JSON格式的字符串
        /// </summary>
        public string ToJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{");
            sb.Append($"\"success\":{Success.ToString().ToLower()},");
            sb.Append($"\"userName\":\"{EscapeJson(UserName ?? "")}\",");
            sb.Append($"\"userId\":{(UserId.HasValue ? UserId.Value.ToString() : "null")},");
            sb.Append($"\"authId\":\"{AuthId}\",");
            sb.Append($"\"authTime\":\"{AuthTime:yyyy-MM-dd HH:mm:ss}\",");
            sb.Append($"\"errorMessage\":\"{EscapeJson(ErrorMessage ?? "")}\"");
            sb.Append("}");
            return sb.ToString();
        }

        private static string EscapeJson(string str)
        {
            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\n", "\\n")
                      .Replace("\r", "\\r")
                      .Replace("\t", "\\t");
        }
    }
}
