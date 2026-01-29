using System;
using System.Text;
using LL;

namespace LL;

public static class AsciiArt
{
    public static void Handle(string[] args)
    {
        if (args.Length == 0 || args[0] == "help")
        {
            UI.PrintInfo("用法:");
            UI.PrintInfo("  art <text>");
            UI.PrintInfo("生成 ASCII 艺术文字。");
            return;
        }

        string text = string.Join(" ", args).ToUpper();
        string art = GenerateArt(text);
        UI.PrintInfo(art);
    }

    private static string GenerateArt(string text)
    {
        // Simple ASCII art using block letters
        string[] lines = new string[5];
        foreach (char c in text)
        {
            string[] charArt = GetCharArt(c);
            for (int i = 0; i < 5; i++)
            {
                lines[i] += charArt[i] + " ";
            }
        }
        return string.Join("\n", lines);
    }

    private static string[] GetCharArt(char c)
    {
        switch (c)
        {
            case 'A': return new[] { " AAA ", "A   A", "AAAAA", "A   A", "A   A" };
            case 'B': return new[] { "BBBB ", "B   B", "BBBB ", "B   B", "BBBB " };
            case 'C': return new[] { " CCC ", "C    ", "C    ", "C    ", " CCC " };
            case 'D': return new[] { "DDDD ", "D   D", "D   D", "D   D", "DDDD " };
            case 'E': return new[] { "EEEEE", "E    ", "EEEEE", "E    ", "EEEEE" };
            case 'F': return new[] { "FFFFF", "F    ", "FFFFF", "F    ", "F    " };
            case 'G': return new[] { " GGG ", "G    ", "G GGG", "G   G", " GGG " };
            case 'H': return new[] { "H   H", "H   H", "HHHHH", "H   H", "H   H" };
            case 'I': return new[] { " III ", "  I  ", "  I  ", "  I  ", " III " };
            case 'J': return new[] { " JJJJ", "    J", "    J", "J   J", " JJJ " };
            case 'K': return new[] { "K   K", "K  K ", "KKK  ", "K  K ", "K   K" };
            case 'L': return new[] { "L    ", "L    ", "L    ", "L    ", "LLLLL" };
            case 'M': return new[] { "M   M", "MM MM", "M M M", "M   M", "M   M" };
            case 'N': return new[] { "N   N", "NN  N", "N N N", "N  NN", "N   N" };
            case 'O': return new[] { " OOO ", "O   O", "O   O", "O   O", " OOO " };
            case 'P': return new[] { "PPPP ", "P   P", "PPPP ", "P    ", "P    " };
            case 'Q': return new[] { " QQQ ", "Q   Q", "Q Q Q", "Q  QQ", " QQQQ" };
            case 'R': return new[] { "RRRR ", "R   R", "RRRR ", "R  R ", "R   R" };
            case 'S': return new[] { " SSS ", "S    ", " SSS ", "    S", " SSS " };
            case 'T': return new[] { "TTTTT", "  T  ", "  T  ", "  T  ", "  T  " };
            case 'U': return new[] { "U   U", "U   U", "U   U", "U   U", " UUU " };
            case 'V': return new[] { "V   V", "V   V", "V   V", " V V ", "  V  " };
            case 'W': return new[] { "W   W", "W   W", "W W W", "WW WW", "W   W" };
            case 'X': return new[] { "X   X", " X X ", "  X  ", " X X ", "X   X" };
            case 'Y': return new[] { "Y   Y", " Y Y ", "  Y  ", "  Y  ", "  Y  " };
            case 'Z': return new[] { "ZZZZZ", "   Z ", "  Z  ", " Z   ", "ZZZZZ" };
            case ' ': return new[] { "     ", "     ", "     ", "     ", "     " };
            default: return new[] { " ??? ", "?   ?", "?????", "?   ?", " ??? " };
        }
    }
}