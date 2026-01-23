using System;
using System.IO;
using Ionic.Zip;
using LL;

namespace LL;

/// <summary>
/// ZIP压缩管理器，支持压缩文件夹并可选密码
/// </summary>
public static class ZipManager
{
    /// <summary>
    /// 压缩文件夹或文件
    /// 用法: zip <source> [outputZip] [password]
    /// </summary>
    /// <param name="args">参数：源路径（文件或文件夹），可选输出ZIP路径，可选密码</param>
    public static void Compress(string[] args)
    {
        if (args.Length < 1)
        {
            UI.PrintError("用法: zip <源路径> [输出ZIP路径] [密码]");
            return;
        }

        string source = args[0];
        string? outputZip = null;
        string? password = null;

        // 解析参数：第一个是source，第二个可能是outputZip或password，第三个是password
        if (args.Length >= 2)
        {
            if (args[1].EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(args[1]))
            {
                outputZip = args[1];
                if (args.Length >= 3)
                {
                    password = args[2];
                }
            }
            else
            {
                password = args[1];
                if (args.Length >= 3)
                {
                    outputZip = args[2];
                }
            }
        }

        if (!File.Exists(source) && !Directory.Exists(source))
        {
            UI.PrintError($"源路径不存在: {source}");
            return;
        }

        // 默认输出路径：源路径的父目录，名为源文件夹/文件名的.zip
        if (string.IsNullOrEmpty(outputZip))
        {
            string sourceDir = Path.GetDirectoryName(source) ?? Directory.GetCurrentDirectory();
            string baseName = Directory.Exists(source) ? Path.GetFileName(source) : Path.GetFileNameWithoutExtension(source);
            outputZip = Path.Combine(sourceDir, baseName + ".zip");

            // 如果存在，添加数字
            int counter = 1;
            string originalOutput = outputZip;
            while (File.Exists(outputZip))
            {
                outputZip = Path.Combine(sourceDir, baseName + counter + ".zip");
                counter++;
            }
        }

        try
        {
            // 确保输出目录存在
            string outputDir = Path.GetDirectoryName(outputZip);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // 如果输出文件已存在，询问覆盖（但默认生成时已避免）
            if (File.Exists(outputZip))
            {
                Console.Write($"文件 {outputZip} 已存在，是否覆盖? (y/n): ");
                string? response = Console.ReadLine();
                if (string.IsNullOrEmpty(response) || !response.ToLower().StartsWith("y"))
                {
                    UI.PrintInfo("操作已取消。");
                    return;
                }
            }

            UI.PrintInfo($"开始压缩: {source}");
            UI.PrintInfo($"输出文件: {outputZip}");
            if (!string.IsNullOrEmpty(password))
            {
                UI.PrintInfo("使用密码保护");
            }

            using (ZipFile zip = new ZipFile())
            {
                if (!string.IsNullOrEmpty(password))
                {
                    zip.Password = password;
                }

                // 添加进度事件
                int lastPercent = -1;
                zip.SaveProgress += (sender, e) =>
                {
                    if (e.EventType == ZipProgressEventType.Saving_AfterWriteEntry)
                    {
                        int percent = (int)((double)e.EntriesSaved / e.EntriesTotal * 100);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            DrawProgressBar(percent, e.EntriesSaved, e.EntriesTotal);
                        }
                    }
                };

                try
                {
                    if (File.Exists(source))
                    {
                        // 压缩单个文件
                        zip.AddFile(source, "");
                    }
                    else
                    {
                        // 压缩文件夹
                        zip.AddDirectory(source, Path.GetFileName(source));
                    }
                }
                catch (Exception ex)
                {
                    UI.PrintError($"添加文件/目录失败: {ex.Message}");
                    return;
                }

                zip.Save(outputZip);
            }

            UI.PrintSuccess($"压缩完成: {outputZip}");
        }
        catch (Exception ex)
        {
            UI.PrintError($"压缩失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 解压ZIP文件
    /// 用法: unzip <zipFile> <outputDir> [password]
    /// </summary>
    /// <param name="args">参数：ZIP文件路径，输出目录，可选密码</param>
    public static void Uncompress(string[] args)
    {
        if (args.Length < 2)
        {
            UI.PrintError("用法: unzip <ZIP文件路径> <输出目录> [密码]");
            return;
        }

        string zipFile = args[0];
        string outputDir = args[1];
        string? password = args.Length > 2 ? args[2] : null;

        if (!File.Exists(zipFile))
        {
            UI.PrintError($"ZIP文件不存在: {zipFile}");
            return;
        }

        try
        {
            // 确保输出目录存在
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            UI.PrintInfo($"开始解压: {zipFile}");
            UI.PrintInfo($"输出目录: {outputDir}");
            if (!string.IsNullOrEmpty(password))
            {
                UI.PrintInfo("使用密码解压");
            }

            using (ZipFile zip = ZipFile.Read(zipFile))
            {
                if (!string.IsNullOrEmpty(password))
                {
                    zip.Password = password;
                }

                zip.ExtractAll(outputDir, ExtractExistingFileAction.OverwriteSilently);
            }

            UI.PrintSuccess($"解压完成: {outputDir}");
        }
        catch (Exception ex)
        {
            UI.PrintError($"解压失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 绘制进度条
    /// </summary>
    private static void DrawProgressBar(int percent, int current, int total)
    {
        int barWidth = 20;
        int filled = (int)(barWidth * percent / 100.0);
        string bar = new string('#', filled) + new string(' ', barWidth - filled);
        Console.Write($"\r[{bar}] {percent}% ({current}/{total})");
        if (percent == 100) Console.WriteLine(); // 完成后换行
    }
}