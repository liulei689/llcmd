using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

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

    public static void ShowMyIp()
    {
        UI.PrintHeader("本机 IP");
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var props = ni.GetIPProperties();
            var v4 = props.UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .ToArray();
            var v6 = props.UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)
                .Select(a => a.Address.ToString())
                .ToArray();

            if (v4.Length == 0 && v6.Length == 0) continue;

            Console.WriteLine($"- {ni.Name}");
            if (v4.Length > 0) Console.WriteLine($"  IPv4: {string.Join(", ", v4)}");
            if (v6.Length > 0) Console.WriteLine($"  IPv6: {string.Join(", ", v6)}");
        }
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
