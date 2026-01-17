using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace LL;

public static class ElevationCommands
{
    public static void Elevate(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            UI.PrintError("仅支持 Windows");
            return;
        }

        if (IsAdministrator())
        {
            UI.PrintSuccess("已是管理员权限");
            return;
        }

        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                UI.PrintError("无法获取当前程序路径");
                return;
            }

            var argLine = string.Join(" ", args.Select(Quote));

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = argLine,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(psi);
            Environment.Exit(0);
        }
        catch (Win32Exception)
        {
            UI.PrintError("已取消管理员授权");
        }
        catch (Exception ex)
        {
            UI.PrintError($"申请管理员权限失败: {ex.Message}");
        }
    }

    public static bool RunElevatedCommand(string command, string[] args)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if (IsAdministrator())
            return false;

        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return false;

            var argLine = string.Join(" ", new[] { "--elevated-run", command }.Concat(args).Select(Quote));

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = argLine,
                UseShellExecute = true,
                Verb = "runas"
            };

            var p = Process.Start(psi);
            return p != null;
        }
        catch (Win32Exception)
        {
            // user canceled UAC
            return false;
        }
        catch (Exception ex)
        {
            UI.PrintError($"申请管理员权限失败: {ex.Message}");
            return false;
        }
    }

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string Quote(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        if (s.Contains(' ') || s.Contains('"'))
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        return s;
    }
}
