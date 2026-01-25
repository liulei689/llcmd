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
            if (args.Length == 0)
            {
                UI.PrintError("用法: hexview <hex字符串>");
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

                int bytesPerLine = 16;
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
    }
}