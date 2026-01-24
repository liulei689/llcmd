using System.Diagnostics;

namespace LL
{
    // 网络控制相关命令
    internal static class NetworkCommands
    {
        public static void ControlNetwork(string[] args)
        {
            if (args.Length < 1)
            {
                UI.PrintError("用法: net [enable|disable] [接口名]");
                UI.PrintInfo("示例: net disable 或 net disable \"Ethernet\"");
                return;
            }

            string action = args[0].ToLower();
            string interfaceName = args.Length > 1 ? string.Join(" ", args.Skip(1)).Trim('"') : "";

            if (action != "enable" && action != "disable")
            {
                UI.PrintError("无效操作: enable 或 disable");
                return;
            }

            try
            {
                if (string.IsNullOrEmpty(interfaceName))
                {
                    // 自动操作活跃网络接口
                    string psCommand = action == "disable" 
                        ? "Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | Disable-NetAdapter -Confirm:$false" 
                        : "Get-NetAdapter | Where-Object { $_.Status -eq 'Disabled' } | Enable-NetAdapter -Confirm:$false";
                    var psi = new ProcessStartInfo("powershell", psCommand)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    var process = Process.Start(psi);
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                    {
                        UI.PrintSuccess($"已{ (action == "enable" ? "启用" : "禁用") }所有活跃网络接口。");
                        if (action == "disable")
                        {
                            UI.PrintInfo("要恢复网络，请运行 'net enable'");
                        }
                    }
                    else
                    {

                        // 提权尝试
                        if (!IsAdministrator())
                        {
                            UI.PrintInfo("已请求管理员权限(UAC)，已在新窗口执行 net " + action);
                            if (ElevationCommands.RunElevatedCommand("net", new[] { action }))
                            {
                                UI.PrintInfo("提权成功，命令已在新窗口执行。");
                            }
                            else
                            {
                                UI.PrintError("提权失败，请手动运行 'admin net " + action + "'。");
                            }
                        }
                    }
                }
                else
                {
                    // 操作指定接口
                    string adminAction = action == "enable" ? "enabled" : "disabled";
                    var psi = new ProcessStartInfo("netsh", $"interface set interface \"{interfaceName}\" admin={adminAction}")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    var process = Process.Start(psi);
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                    {
                        UI.PrintSuccess($"网络接口 '{interfaceName}' 已{ (action == "enable" ? "启用" : "禁用") }。");
                    }
                    else
                    {
                        // 失败，可能是权限不足
                        if (!IsAdministrator())
                        {
                            UI.PrintInfo("已请求管理员权限(UAC)，已在新窗口执行 net " + action + " \"" + interfaceName + "\"");
                            if (ElevationCommands.RunElevatedCommand("net", new[] { action, interfaceName }))
                            {
                                UI.PrintInfo("提权成功，命令已在新窗口执行。");
                            }
                            else
                            {
                                UI.PrintError("提权失败，请手动运行 'admin net " + action + " \"" + interfaceName + "\"'。");
                            }
                        }
                        else
                        {
                            UI.PrintError("操作失败，请检查接口名是否正确。");
                            UI.PrintInfo("使用 'ip' 命令查看网络接口。");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UI.PrintError($"操作失败: {ex.Message}");
            }
        }

        static bool IsAdministrator()
        {
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }
    }
}