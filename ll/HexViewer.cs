using System;
using System.Linq;
using System.Text;
using LL;

namespace LL
{
    internal static class HexViewer
    {
        public static void ViewHex(string[] args)
        {
            if (args.Length > 0 && args[0] == "-b")
            {
                ViewBinary(args.Skip(1).ToArray());
                return;
            }

            if (args.Length == 0)
            {
                UI.PrintError("用法: hexview [-b] <hex字符串>");
                return;
            }

            string hexInput = string.Join("", args).Replace(" ", "").ToUpper();
            if (hexInput.Length % 2 != 0)
            {
                UI.PrintError("Hex字符串长度必须为偶数。");
                return;
            }

            try
            {
                byte[] bytes = HexStringToBytes(hexInput);
                UI.PrintInfo($"总字节数: {bytes.Length}");

                int bytesPerLine = 6; // 默认单行6字节
                int lineIndex = 0;
                for (int i = 0; i < bytes.Length; i += bytesPerLine)
                {
                    int lineBytes = Math.Min(bytesPerLine, bytes.Length - i);

                    // 索引行
                    StringBuilder indexLine = new StringBuilder();
                    indexLine.Append("\u001b[33m"); // 黄色
                    for (int j = 0; j < lineBytes; j++)
                    {
                        indexLine.Append($"{lineIndex:D3} ");
                        lineIndex++;
                    }
                    indexLine.Append("\u001b[0m");
                    Console.WriteLine(indexLine.ToString());

                    // Hex行
                    StringBuilder hexLine = new StringBuilder();
                    hexLine.Append("\u001b[32m"); // 绿色
                    for (int j = 0; j < lineBytes; j++)
                    {
                        hexLine.Append($"{bytes[i + j]:X2}  ");
                    }
                    hexLine.Append("\u001b[0m");
                    Console.WriteLine(hexLine.ToString());

                    // ASCII行
                    StringBuilder asciiLine = new StringBuilder();
                    asciiLine.Append("\u001b[36m"); // 青色
                    for (int j = 0; j < lineBytes; j++)
                    {
                        char c = (char)bytes[i + j];
                        asciiLine.Append(char.IsControl(c) || c > 127 ? '.' : c);
                    }
                    asciiLine.Append("\u001b[0m");
                    Console.WriteLine(asciiLine.ToString());

                    Console.WriteLine();
                }
            }
            catch
            {
                UI.PrintError("无效的Hex字符串。");
            }
        }

        private static byte[] HexStringToBytes(string hex)
        {
            return Enumerable.Range(0, hex.Length / 2)
                .Select(x => Convert.ToByte(hex.Substring(x * 2, 2), 16))
                .ToArray();
        }

        private static void ViewBinary(string[] args)
        {
            if (args.Length == 0)
            {
                UI.PrintError("用法: hexview -b <hex字符串>");
                return;
            }

            string hexInput = string.Join("", args).Replace(" ", "").ToUpper();
            if (hexInput.Length % 2 != 0)
            {
                UI.PrintError("Hex字符串长度必须为偶数。");
                return;
            }

            try
            {
                byte[] bytes = HexStringToBytes(hexInput);
                UI.PrintInfo($"总字节数: {bytes.Length}");

                // 颜色数组：位索引0~7对应不同颜色
                string[] bitColors = {
                    "\u001b[31m", // 位0: 红色
                    "\u001b[32m", // 位1: 绿色
                    "\u001b[33m", // 位2: 黄色
                    "\u001b[34m", // 位3: 蓝色
                    "\u001b[35m", // 位4: 紫色
                    "\u001b[36m", // 位5: 青色
                    "\u001b[37m", // 位6: 白色
                    "\u001b[91m"  // 位7: 亮红色
                };

                int bytesPerLine = 6; // 默认二进制显示每行6字节
                int lineIndex = 0;
                for (int i = 0; i < bytes.Length; i += bytesPerLine)
                {
                    int lineBytes = Math.Min(bytesPerLine, bytes.Length - i);

                    // 字节索引行
                    StringBuilder byteIndexLine = new StringBuilder();
                    byteIndexLine.Append("\u001b[33m"); // 黄色
                    for (int j = 0; j < lineBytes; j++)
                    {
                        byteIndexLine.Append($"{lineIndex:D3}");
                        byteIndexLine.Append(new string(' ', 13)); // 填充到15字符
                        if (j < lineBytes - 1) byteIndexLine.Append("|");
                        lineIndex++;
                    }
                    byteIndexLine.Append("\u001b[0m");
                    Console.WriteLine(byteIndexLine.ToString());

                    // 位索引行
                    StringBuilder bitIndexLine = new StringBuilder();
                    bitIndexLine.Append("\u001b[36m"); // 青色
                    for (int j = 0; j < lineBytes; j++)
                    {
                        bitIndexLine.Append("7 6 5 4 3 2 1 0 ");
                        if (j < lineBytes - 1) bitIndexLine.Append("|");
                    }
                    bitIndexLine.Append("\u001b[0m");
                    Console.WriteLine(bitIndexLine.ToString());

                    // Binary行
                    StringBuilder binaryLine = new StringBuilder();
                    for (int j = 0; j < lineBytes; j++)
                    {
                        byte b = bytes[i + j];
                        for (int bit = 0; bit < 8; bit++)
                        {
                            int bitValue = (b >> (7 - bit)) & 1;
                            binaryLine.Append(bitColors[bit]);
                            binaryLine.Append(bitValue);
                            binaryLine.Append(" ");
                            binaryLine.Append("\u001b[0m");
                        }
                        if (j < lineBytes - 1) binaryLine.Append("|");
                    }
                    Console.WriteLine(binaryLine.ToString());

                    // Hex行
                    StringBuilder hexLine = new StringBuilder();
                    hexLine.Append("\u001b[32m"); // 绿色
                    for (int j = 0; j < lineBytes; j++)
                    {
                        hexLine.Append($"{bytes[i + j]:X2}");
                        hexLine.Append(new string(' ', 14)); // 填充到15字符
                        if (j < lineBytes - 1) hexLine.Append("|");
                    }
                    hexLine.Append("\u001b[0m");
                    Console.WriteLine(hexLine.ToString());

                    // ASCII行
                    StringBuilder asciiLine = new StringBuilder();
                    asciiLine.Append("\u001b[36m"); // 青色
                    for (int j = 0; j < lineBytes; j++)
                    {
                        char c = (char)bytes[i + j];
                        asciiLine.Append(char.IsControl(c) || c > 127 ? '.' : c);
                        asciiLine.Append(new string(' ', 15)); // 填充到15字符
                        if (j < lineBytes - 1) asciiLine.Append("|");
                    }
                    asciiLine.Append("\u001b[0m");
                    Console.WriteLine(asciiLine.ToString());

                    Console.WriteLine();
                }
            }
            catch
            {
                UI.PrintError("无效的Hex字符串。");
            }
        }
    }
}