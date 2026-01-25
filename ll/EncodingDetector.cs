using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using UtfUnknown;

namespace LL
{
    // 文件编码检测工具
    internal static class EncodingDetector
    {
        public static void DetectEncoding(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: encoding <path>");
                return;
            }

            string path = args[0];
            if (File.Exists(path))
            {
                DetectFileEncoding(path);
            }
            else if (Directory.Exists(path))
            {
                AnalyzeFolderEncoding(path);
            }
            else
            {
                UI.PrintError("路径不存在或无效。");
            }
        }

        private static void DetectFileEncoding(string filePath)
        {
            try
            {
                var result = CharsetDetector.DetectFromFile(filePath);
                if (result.Detected != null)
                {
                    var encodingName = result.Detected.EncodingName;
                    var confidence = result.Detected.Confidence;
                    UI.PrintInfo($"{filePath}: {encodingName} (confidence {confidence:F2})");
                }
                else
                {
                    UI.PrintInfo($"{filePath}: Unknown");
                }
            }
            catch (Exception ex)
            {
                UI.PrintError($"{filePath}: 检测失败 - {ex.Message}");
            }
        }

        private static void AnalyzeFolderEncoding(string folderPath)
        {
            UI.PrintHeader($"分析文件夹编码: {folderPath}");
            try
            {
                var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories).ToList();
                foreach (var file in files)
                {
                    try
                    {
                        var result = CharsetDetector.DetectFromFile(file);
                        if (result.Detected != null)
                        {
                            var encodingName = result.Detected.EncodingName;
                            var confidence = result.Detected.Confidence;
                            Console.WriteLine($"{file}: {encodingName} (confidence {confidence:F2})");
                        }
                        else
                        {
                            Console.WriteLine($"{file}: Unknown");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{file}: 检测失败 - {ex.Message}");
                    }
                }
                UI.PrintSuccess($"分析完成，共 {files.Count} 个文件");
            }
            catch (Exception ex)
            {
                UI.PrintError($"分析失败: {ex.Message}");
            }
        }
    }
}