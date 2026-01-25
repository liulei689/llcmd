using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.IO;
using LL;

namespace LL
{
    internal static class ImageAnalyzer
    {
        public static void AnalyzeImage(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: image <path>");
                return;
            }

            string path = args[0];
            if (!File.Exists(path))
            {
                UI.PrintError("文件不存在。");
                return;
            }

            try
            {
                var fileInfo = new FileInfo(path);
                using var bitmap = new Bitmap(path);
                int width = bitmap.Width;
                int height = bitmap.Height;
                int totalPixels = width * height;
                string format = GetImageFormat(path);
                bool hasAlpha = Image.IsAlphaPixelFormat(bitmap.PixelFormat);
                bool isGrayscale = IsGrayscale(bitmap);

                UI.PrintHeader("图片信息");
                UI.PrintResult("文件路径", path);
                UI.PrintResult("文件大小", $"{fileInfo.Length} 字节 ({(fileInfo.Length / 1024.0):F2} KB)");
                UI.PrintResult("创建时间", fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"));
                UI.PrintResult("修改时间", fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                UI.PrintResult("图片格式", format);
                UI.PrintResult("尺寸", $"{width} x {height}");
                UI.PrintResult("总像素数", totalPixels.ToString());
                UI.PrintResult("颜色模式", hasAlpha ? "RGBA (带透明)" : "RGB");
                UI.PrintResult("是否灰度", isGrayscale ? "是" : "否");

                var colorCounts = new Dictionary<Color, int>();
                int alphaPixels = 0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color pixel = bitmap.GetPixel(x, y);
                        if (pixel.A < 255) alphaPixels++;
                        if (colorCounts.ContainsKey(pixel))
                            colorCounts[pixel]++;
                        else
                            colorCounts[pixel] = 1;
                    }
                }

                UI.PrintResult("透明像素数", $"{alphaPixels} ({(double)alphaPixels / totalPixels * 100:F2}%)");

                double entropy = CalculateEntropy(colorCounts, totalPixels);
                UI.PrintResult("颜色熵", $"{entropy:F4} (多样性指标，0-8+)");

                UI.PrintHeader("颜色分布 (按像素数量降序)");
                int count = 0;
                foreach (var kvp in colorCounts.OrderByDescending(x => x.Value))
                {
                    if (count >= 20) break;
                    string colorName = GetColorName(kvp.Key);
                    string hex = $"#{kvp.Key.R:X2}{kvp.Key.G:X2}{kvp.Key.B:X2}";
                    if (kvp.Key.A < 255) hex = $"#{kvp.Key.A:X2}{kvp.Key.R:X2}{kvp.Key.G:X2}{kvp.Key.B:X2}";
                    UI.PrintResult($"{colorName} ({hex})", $"{kvp.Value} 个像素 ({(double)kvp.Value / totalPixels * 100:F2}%)");
                    count++;
                }

                UI.PrintResult("唯一颜色数", colorCounts.Count.ToString());

                // 平均颜色
                long totalR = 0, totalG = 0, totalB = 0, totalA = 0;
                foreach (var kvp in colorCounts)
                {
                    totalR += (long)kvp.Key.R * kvp.Value;
                    totalG += (long)kvp.Key.G * kvp.Value;
                    totalB += (long)kvp.Key.B * kvp.Value;
                    totalA += (long)kvp.Key.A * kvp.Value;
                }
                Color avgColor = Color.FromArgb((int)(totalA / totalPixels), (int)(totalR / totalPixels), (int)(totalG / totalPixels), (int)(totalB / totalPixels));
                UI.PrintResult("平均颜色", $"RGBA({avgColor.R},{avgColor.G},{avgColor.B},{avgColor.A}) #{avgColor.A:X2}{avgColor.R:X2}{avgColor.G:X2}{avgColor.B:X2}");

                // 亮度分析
                double avgBrightness = CalculateAverageBrightness(colorCounts, totalPixels);
                UI.PrintResult("平均亮度", $"{avgBrightness:F2} (0-255)");

                // EXIF 信息
                UI.PrintHeader("EXIF 信息");
                try
                {
                    var exif = bitmap.PropertyItems;
                    if (exif.Length > 0)
                    {
                        foreach (var item in exif)
                        {
                            string value = GetExifValue(item);
                            if (!string.IsNullOrEmpty(value))
                            {
                                UI.PrintResult(GetExifTagName(item.Id), value);
                            }
                        }
                    }
                    else
                    {
                        UI.PrintResult("EXIF 数据", "无");
                    }
                }
                catch
                {
                    UI.PrintResult("EXIF 数据", "读取失败");
                }

                // 亮度直方图
                UI.PrintHeader("亮度分布");
                var brightnessHistogram = new int[256];
                foreach (var kvp in colorCounts)
                {
                    int brightness = (int)(0.299 * kvp.Key.R + 0.587 * kvp.Key.G + 0.114 * kvp.Key.B);
                    brightnessHistogram[brightness] += kvp.Value;
                }
                int maxBrightness = brightnessHistogram.Max();
                for (int i = 0; i < 256; i += 16)
                {
                    int histCount = 0;
                    for (int j = 0; j < 16 && i + j < 256; j++)
                        histCount += brightnessHistogram[i + j];
                    string bar = new string('█', (int)(histCount / (double)totalPixels * 50));
                    UI.PrintResult($"{i:D3}-{Math.Min(i + 15, 255):D3}", $"{bar} ({histCount})");
                }

                // 对比度
                double contrast = CalculateContrast(colorCounts, totalPixels);
                UI.PrintResult("对比度", $"{contrast:F2} (0-1, 越高对比越强)");

                // 饱和度
                double saturation = CalculateAverageSaturation(colorCounts, totalPixels);
                UI.PrintResult("平均饱和度", $"{saturation:F2} (0-1, 越高颜色越鲜艳)");
            }
            catch (Exception ex)
            {
                UI.PrintError($"分析失败: {ex.Message}");
            }
        }

        private static string GetImageFormat(string path)
        {
            try
            {
                using var img = Image.FromFile(path);
                if (img.RawFormat.Equals(System.Drawing.Imaging.ImageFormat.Jpeg))
                    return "JPEG";
                if (img.RawFormat.Equals(System.Drawing.Imaging.ImageFormat.Png))
                    return "PNG";
                if (img.RawFormat.Equals(System.Drawing.Imaging.ImageFormat.Bmp))
                    return "BMP";
                if (img.RawFormat.Equals(System.Drawing.Imaging.ImageFormat.Gif))
                    return "GIF";
                if (img.RawFormat.Equals(System.Drawing.Imaging.ImageFormat.Tiff))
                    return "TIFF";
                return "未知";
            }
            catch
            {
                return "未知";
            }
        }

        private static string GetColorName(Color color)
        {
            // 简单颜色名称映射
            if (color.R == 255 && color.G == 0 && color.B == 0) return "红色";
            if (color.R == 0 && color.G == 255 && color.B == 0) return "绿色";
            if (color.R == 0 && color.G == 0 && color.B == 255) return "蓝色";
            if (color.R == 255 && color.G == 255 && color.B == 0) return "黄色";
            if (color.R == 255 && color.G == 0 && color.B == 255) return "品红";
            if (color.R == 0 && color.G == 255 && color.B == 255) return "青色";
            if (color.R == 255 && color.G == 255 && color.B == 255) return "白色";
            if (color.R == 0 && color.G == 0 && color.B == 0) return "黑色";
            if (color.R == 128 && color.G == 128 && color.B == 128) return "灰色";
            return "自定义";
        }

        private static bool IsGrayscale(Bitmap bitmap)
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    if (pixel.R != pixel.G || pixel.G != pixel.B)
                        return false;
                }
            }
            return true;
        }

        private static double CalculateEntropy(Dictionary<Color, int> colorCounts, int totalPixels)
        {
            double entropy = 0;
            foreach (var count in colorCounts.Values)
            {
                double p = (double)count / totalPixels;
                entropy -= p * Math.Log(p, 2);
            }
            return entropy;
        }

        private static double CalculateAverageBrightness(Dictionary<Color, int> colorCounts, int totalPixels)
        {
            double totalBrightness = 0;
            foreach (var kvp in colorCounts)
            {
                double brightness = 0.299 * kvp.Key.R + 0.587 * kvp.Key.G + 0.114 * kvp.Key.B;
                totalBrightness += brightness * kvp.Value;
            }
            return totalBrightness / totalPixels;
        }

        private static double CalculateContrast(Dictionary<Color, int> colorCounts, int totalPixels)
        {
            double minBrightness = 255;
            double maxBrightness = 0;
            foreach (var kvp in colorCounts)
            {
                double brightness = 0.299 * kvp.Key.R + 0.587 * kvp.Key.G + 0.114 * kvp.Key.B;
                if (brightness < minBrightness) minBrightness = brightness;
                if (brightness > maxBrightness) maxBrightness = brightness;
            }
            return (maxBrightness - minBrightness) / 255.0;
        }

        private static double CalculateAverageSaturation(Dictionary<Color, int> colorCounts, int totalPixels)
        {
            double totalSaturation = 0;
            foreach (var kvp in colorCounts)
            {
                double r = kvp.Key.R / 255.0;
                double g = kvp.Key.G / 255.0;
                double b = kvp.Key.B / 255.0;
                double max = Math.Max(r, Math.Max(g, b));
                double min = Math.Min(r, Math.Min(g, b));
                double saturation = max == 0 ? 0 : (max - min) / max;
                totalSaturation += saturation * kvp.Value;
            }
            return totalSaturation / totalPixels;
        }

        private static string GetExifValue(System.Drawing.Imaging.PropertyItem item)
        {
            try
            {
                switch (item.Type)
                {
                    case 1: // BYTE
                        return item.Value[0].ToString();
                    case 2: // ASCII
                        return System.Text.Encoding.ASCII.GetString(item.Value).Trim('\0');
                    case 3: // SHORT
                        return BitConverter.ToUInt16(item.Value, 0).ToString();
                    case 4: // LONG
                        return BitConverter.ToUInt32(item.Value, 0).ToString();
                    case 5: // RATIONAL
                        uint num = BitConverter.ToUInt32(item.Value, 0);
                        uint den = BitConverter.ToUInt32(item.Value, 4);
                        return $"{num}/{den}";
                    case 7: // UNDEFINED
                        return "未定义";
                    default:
                        return "未知类型";
                }
            }
            catch
            {
                return "解析失败";
            }
        }

        private static string GetExifTagName(int id)
        {
            switch (id)
            {
                case 0x010E: return "图像描述";
                case 0x010F: return "制造商";
                case 0x0110: return "型号";
                case 0x0112: return "方向";
                case 0x011A: return "X分辨率";
                case 0x011B: return "Y分辨率";
                case 0x0128: return "分辨率单位";
                case 0x0132: return "修改时间";
                case 0x8298: return "版权";
                case 0x8769: return "EXIF 子 IFD";
                case 0x8825: return "GPS 子 IFD";
                case 0x9003: return "原始拍摄时间";
                case 0x9004: return "数字化时间";
                case 0x920A: return "焦距";
                case 0xA002: return "图像宽度";
                case 0xA003: return "图像高度";
                default: return $"标签 {id:X4}";
            }
        }
    }
}