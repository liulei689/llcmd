using System.Text;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using LL;

namespace LL;

public static class Utils
{
    public static bool TryParseTime(string input, out double totalSeconds)
    {
        totalSeconds = 0;
        input = input.ToLower().Trim();
        double val = 0;

        try
        {
            if (input.EndsWith("s") && double.TryParse(input[..^1], out val))
                totalSeconds = val;
            else if (input.EndsWith("m") && double.TryParse(input[..^1], out val))
                totalSeconds = val * 60;
            else if (input.EndsWith("h") && double.TryParse(input[..^1], out val))
                totalSeconds = val * 3600;
            else if (double.TryParse(input, out val))
                totalSeconds = val * 3600; // 默认为小时
            else
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SendEmailTo(string subject, string body)
    {
        UI.PrintInfo("正在发送通知邮件...");
        try
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("config.json", optional: true);
            var config = builder.Build();

            string smtpHost = config["Email:SmtpHost"] ?? "smtp.qq.com";
            int smtpPort = int.Parse(config["Email:SmtpPort"] ?? "587");
            string username = config["Email:Username"] ?? "1243500742@qq.com";
            string password = config["Email:Password"] ?? "***";
            string fromAddress = config["Email:FromAddress"] ?? "1243500742@qq.com";
            string fromName = config["Email:FromName"] ?? "给您关机啦！";
            string toAddress = config["Email:ToAddress"] ?? "799942292@qq.com";

            using (MailMessage mailMessage = new MailMessage())
            using (SmtpClient smtpClient = new SmtpClient(smtpHost, smtpPort))
            {
                mailMessage.To.Add(toAddress);
                mailMessage.Body = Environment.MachineName+ body;
                mailMessage.IsBodyHtml = true;
                mailMessage.BodyEncoding = Encoding.UTF8;
                mailMessage.From = new MailAddress(fromAddress, subject); // Use subject as FromName
                mailMessage.Subject = subject;
                mailMessage.SubjectEncoding = Encoding.UTF8;

                smtpClient.EnableSsl = true;
                smtpClient.Credentials = new NetworkCredential(username, password);

                smtpClient.Send(mailMessage);
                UI.PrintSuccess("邮件发送成功!");
                LogManager.Log("Info", "Email", $"邮件发送成功: {body}");
            }
        }
        catch (Exception ex)
        {
            UI.PrintError($"邮件发送失败: {ex.Message}");
            LogManager.Log("Error", "Email", $"邮件发送失败: {ex.Message}");
            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "log.txt"), $"{DateTime.Now}: {ex.Message}\r\n");
            }
            catch { }
        }
    }

    public static string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = (decimal)bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return string.Format("{0:n1} {1}", number, suffixes[counter]);
    }

    public static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
