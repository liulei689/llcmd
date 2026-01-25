using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using LL;

namespace LL
{
    internal static class BinaryFileReader
    {
        public static void ReadBinary(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: readbin <path> [binary]");
                return;
            }

            string path = args[0];
            bool isBinary = args.Length > 1 && args[1].ToLower() == "binary";

            if (!File.Exists(path))
            {
                UI.PrintError("文件不存在。");
                return;
            }

            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    long fileSize = fs.Length;
                    if (fileSize > 10L * 1024 * 1024) // 10MB limit
                    {
                        UI.PrintError("文件太大，仅支持10MB以下文件。");
                        return;
                    }

                    byte[] buffer = new byte[4096];
                    int bytesRead;
                    StringBuilder sb = new StringBuilder();
                    long totalBytesRead = 0;

                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        for (int i = 0; i < bytesRead; i++)
                        {
                            if (isBinary)
                            {
                                sb.Append(Convert.ToString(buffer[i], 2).PadLeft(8, '0'));
                                sb.Append(' ');
                            }
                            else
                            {
                                if (totalBytesRead % 16 == 0 && totalBytesRead > 0) sb.AppendLine();
                                sb.Append(buffer[i].ToString("X2"));
                                sb.Append(' ');
                            }
                            totalBytesRead++;
                        }
                    }

                    string content = sb.ToString();
                    string tempFile = Path.GetTempFileName() + ".txt";
                    File.WriteAllText(tempFile, content);

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "notepad.exe",
                        Arguments = $"\"{tempFile}\"",
                        UseShellExecute = true
                    });

                    UI.PrintSuccess($"已打开临时文件: {tempFile} (大小: {fileSize} 字节)");
                }
            }
            catch (Exception ex)
            {
                UI.PrintError($"读取失败: {ex.Message}");
            }
        }
    }
}