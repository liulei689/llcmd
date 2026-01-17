using System.Diagnostics;

namespace LL;

public static class QuickCommands
{
    public static void OpenTaskManager() => Start("taskmgr");

    public static void OpenDeviceManager() => Start("devmgmt.msc");

    public static void OpenControlPanel() => Start("control");

    public static void OpenSettings() => Start("explorer", "ms-settings:");

    public static void OpenNetworkSettings() => Start("explorer", "ms-settings:network");

    public static void OpenSoundSettings() => Start("explorer", "ms-settings:sound");

    public static void OpenDisplaySettings() => Start("explorer", "ms-settings:display");

    public static void OpenStorageSettings() => Start("explorer", "ms-settings:storagesense");

    public static void OpenDesktopFolder() => Start("explorer", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

    public static void OpenTempFolder() => Start("explorer", Path.GetTempPath());

    public static void OpenRecycleBin() => Start("explorer", "shell:RecycleBinFolder");

    public static void OpenSnippingTool()
    {
        // Windows 10/11
        Start("snippingtool");
        // If missing, user can install; keep silent.
    }

    public static void FlushDns()
    {
        RunHidden("ipconfig", "/flushdns");
    }

    public static void NetFix()
    {
        // Some steps require admin; still safe to attempt.
        RunHidden("ipconfig", "/flushdns");
        RunHidden("netsh", "winsock reset");
        RunHidden("netsh", "int ip reset");
    }

    private static void Start(string fileName, string? args = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(fileName, args ?? string.Empty)
            {
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private static void RunHidden(string fileName, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(fileName, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch
        {
        }
    }
}
