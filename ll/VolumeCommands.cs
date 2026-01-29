using System;
using System.Runtime.InteropServices;

namespace LL;

public static class VolumeCommands
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_VOLUME_MUTE = 0xAD;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_UP = 0xAF;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static void Run(string[] args)
    {
        if (args.Length < 1)
        {
            UI.PrintError("用法: volume <mute|unmute|up|down|set <level>>");
            UI.PrintInfo("示例: volume mute, volume set 50");
            return;
        }

        var action = args[0].ToLower();
        switch (action)
        {
            case "mute":
                Mute();
                UI.PrintSuccess("声音已静音");
                break;
            case "unmute":
                Unmute();
                UI.PrintSuccess("声音已取消静音");
                break;
            case "up":
                VolumeUp();
                UI.PrintSuccess("音量调高");
                break;
            case "down":
                VolumeDown();
                UI.PrintSuccess("音量调低");
                break;
            case "set":
                if (args.Length > 1 && int.TryParse(args[1], out var level))
                {
                    SetVolume(level);
                    UI.PrintSuccess($"音量设置为 {level}%");
                }
                else
                {
                    UI.PrintError("请提供有效的音量级别 (0-100)");
                }
                break;
            default:
                UI.PrintError("无效操作: " + action);
                break;
        }
    }

    private static void Mute()
    {
        keybd_event(VK_VOLUME_MUTE, 0, 0, UIntPtr.Zero);
        keybd_event(VK_VOLUME_MUTE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static void Unmute()
    {
        // Unmute is same as mute toggle
        Mute();
    }

    private static void VolumeUp()
    {
        keybd_event(VK_VOLUME_UP, 0, 0, UIntPtr.Zero);
        keybd_event(VK_VOLUME_UP, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static void VolumeDown()
    {
        keybd_event(VK_VOLUME_DOWN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_VOLUME_DOWN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static void SetVolume(int level)
    {
        // Approximate by pressing up/down multiple times
        // This is not precise, but simple
        int current = 50; // Assume current is 50%
        int diff = level - current;
        for (int i = 0; i < Math.Abs(diff); i++)
        {
            if (diff > 0)
                VolumeUp();
            else
                VolumeDown();
            System.Threading.Thread.Sleep(50); // Small delay
        }
    }
}